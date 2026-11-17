using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Persistence.Messaging;

public interface IRawMessageReceiver<T>
{
    Task ReceiveAsync(MessageEnvelope<JsonElement> message, CancellationToken cancellationToken);
}

public sealed class InboxMessageReceiver<T, TDbContext>(
    TDbContext db, IMessageHandler<T> handler, TimeProvider clock,
    ILogger<InboxMessageReceiver<T, TDbContext>> logger, string consumer,
    bool transactional = true, int maximumAttempts = 8) : IMessageReceiver<T>, IRawMessageReceiver<T>
    where TDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string ContractName = MessageContracts.For(typeof(T)).Name;

    public Task ReceiveAsync(MessageEnvelope<T> message, CancellationToken cancellationToken) =>
        ReceiveAsync(new MessageEnvelope<JsonElement>(message.Id,
            JsonSerializer.SerializeToElement(message.Payload, JsonOptions), message.Metadata), cancellationToken);

    public async Task ReceiveAsync(MessageEnvelope<JsonElement> message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var receipt = await FindAsync(message.Id, cancellationToken);
        if (receipt?.CompletedAtUtc is not null || receipt?.DeadLetteredAtUtc is not null) return;
        if (receipt?.NextAttemptAtUtc is { } retry && retry - clock.GetUtcNow() is { } delay && delay > TimeSpan.Zero)
            await Task.Delay(delay, clock, cancellationToken);
        IDbContextTransaction? transaction = null;
        try
        {
            if (transactional) transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (message.Id == Guid.Empty || !MessageContracts.Accepts<T>(message.Metadata))
                throw new MessageRejectedException("The message identity, contract name or schema version is invalid.");
            var payload = message.Payload.Deserialize<T>(JsonOptions)
                ?? throw new MessageRejectedException("The message payload is missing.");
            receipt ??= InboxMessage.Create(consumer, ContractName, message.Id,
                JsonSerializer.Serialize(message, JsonOptions), clock.GetUtcNow());
            if (transactional)
            {
                if (db.Entry(receipt).State == EntityState.Detached) db.Add(receipt);
                receipt.Claim();
                // Establish the unique claim inside the same transaction as the business changes.
                await db.SaveChangesAsync(cancellationToken);
            }
            using var context = MessageContext.Begin(message.Id, message.Metadata);
            await handler.HandleAsync(new(message.Id, payload, message.Metadata), cancellationToken);
            if (!transactional)
            {
                // Resumable workflows retain their existing commit order and idempotent steps.
                if (db.Entry(receipt).State == EntityState.Detached) db.Add(receipt);
                receipt.Claim();
            }
            receipt.Complete(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                transaction = null;
            }
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception error)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                transaction = null;
            }
            db.ChangeTracker.Clear();
            // A concurrent successful delivery is a duplicate only when its committed completion exists.
            var persisted = await FindAsync(message.Id, cancellationToken);
            if (persisted?.CompletedAtUtc is not null || persisted?.DeadLetteredAtUtc is not null) return;
            persisted ??= InboxMessage.Create(consumer, ContractName, message.Id,
                JsonSerializer.Serialize(message, JsonOptions), clock.GetUtcNow());
            if (db.Entry(persisted).State == EntityState.Detached) db.Add(persisted);
            persisted.Fail(clock.GetUtcNow(), error, maximumAttempts);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError(error, "Message {MessageId} for {Consumer} failed on attempt {Attempt}; parked: {Parked}.",
                message.Id, consumer, persisted.Attempts, persisted.DeadLetteredAtUtc is not null);
            if (persisted.DeadLetteredAtUtc is not null) return;
            // Broker redelivery and local polling share this persisted delay; no immediate requeue loop.
            if (persisted.NextAttemptAtUtc is { } next)
            {
                var remaining = next - clock.GetUtcNow();
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, clock, cancellationToken);
            }
            throw;
        }
        finally { if (transaction is not null) await transaction.DisposeAsync(); }
    }

    private Task<InboxMessage?> FindAsync(Guid id, CancellationToken token) => db.Set<InboxMessage>()
        .SingleOrDefaultAsync(row => row.Consumer == consumer && row.MessageType == ContractName && row.MessageId == id, token);
}

public static class MessageReceiverServiceCollectionExtensions
{
    public static IServiceCollection AddMessageReceiver<T, TDbContext, THandler>(this IServiceCollection services,
        string consumer, bool transactional = true, int maximumAttempts = 8)
        where TDbContext : DbContext where THandler : class, IMessageHandler<T>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        if (maximumAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMessageHandler<T>, THandler>();
        services.AddScoped(provider => new InboxMessageReceiver<T, TDbContext>(
            provider.GetRequiredService<TDbContext>(), provider.GetRequiredService<IMessageHandler<T>>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<InboxMessageReceiver<T, TDbContext>>>(),
            consumer, transactional, maximumAttempts));
        services.AddScoped<IMessageReceiver<T>>(provider => provider.GetRequiredService<InboxMessageReceiver<T, TDbContext>>());
        services.AddScoped<IRawMessageReceiver<T>>(provider => provider.GetRequiredService<InboxMessageReceiver<T, TDbContext>>());
        services.AddSingleton<IHostedService>(provider => new InboxReplayDispatcher<T, TDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<InboxReplayDispatcher<T, TDbContext>>>(), consumer));
        return services;
    }
}
