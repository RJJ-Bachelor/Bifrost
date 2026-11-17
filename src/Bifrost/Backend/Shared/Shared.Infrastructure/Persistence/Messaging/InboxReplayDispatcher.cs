using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Persistence.Messaging;

// A parked message may already have been acknowledged by its original transport.
// Explicit replay therefore reads the retained envelope, not the original queue.
public sealed class InboxReplayDispatcher<T, TDbContext>(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<InboxReplayDispatcher<T, TDbContext>> logger, string consumer) : BackgroundService where TDbContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        do
        {
            try { await ReplayPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Inbox replay for {Consumer} failed.", consumer); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task ReplayPendingAsync(CancellationToken cancellationToken)
    {
        string[] envelopes;
        var type = MessageContracts.For(typeof(T)).Name;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
            envelopes = await db.Set<InboxMessage>().AsNoTracking()
                .Where(row => row.Consumer == consumer && row.MessageType == type &&
                    row.ReplayRequestedAtUtc != null && row.CompletedAtUtc == null && row.DeadLetteredAtUtc == null &&
                    (row.NextAttemptAtUtc == null || row.NextAttemptAtUtc <= clock.GetUtcNow()))
                .OrderBy(row => row.ReplayRequestedAtUtc).Take(50).Select(row => row.Envelope).ToArrayAsync(cancellationToken);
        }
        foreach (var envelope in envelopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var receiver = scopes.CreateAsyncScope();
                var message = JsonSerializer.Deserialize<MessageEnvelope<JsonElement>>(envelope,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException("The retained envelope is missing.");
                await receiver.ServiceProvider.GetRequiredService<IRawMessageReceiver<T>>().ReceiveAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) { logger.LogError(error, "Replayed message for {Consumer} failed; its retry state is retained.", consumer); }
        }
    }
}
