using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Persistence.Messaging;

internal sealed class RabbitMqMessageBus : IMessageBus, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _publisherLock = new(1, 1);
    private readonly HashSet<string> _declaredExchanges = new(StringComparer.Ordinal);
    private IConnection? _connection;
    private IChannel? _publisherChannel;
    private int _disposed;

    public RabbitMqMessageBus(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException(
                "The 'rabbitmq' connection string is not configured.");
    }

    public async Task PublishAsync(
        Guid id,
        object payload,
        string exchangeName,
        string routingKey,
        CancellationToken cancellationToken = default,
        string? messageType = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("A message id is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);

        using var activity = MessageBusTelemetry.StartPublishActivity(exchangeName, routingKey, id.ToString());
        try
        {
            var message = new
            {
                id,
                payload
            };
            var body = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = id.ToString(),
                Type = messageType ?? payload.GetType().FullName
            };
            MessageBusTelemetry.InjectContext(properties);
            activity?.SetTag("messaging.message.body.size", body.Length);

            // Channels are long lived but must not be published to concurrently. Keep the
            // confirmation inside the gate: returning still means the broker accepted it.
            await _publisherLock.WaitAsync(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (_publisherChannel is null || !_publisherChannel.IsOpen)
                {
                    await ResetPublisherChannelAsync();
                    var connection = await GetConnectionAsync(cancellationToken);
                    _publisherChannel = await connection.CreateChannelAsync(
                        new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                        cancellationToken);
                }

                if (!_declaredExchanges.Contains(exchangeName))
                {
                    await _publisherChannel.ExchangeDeclareAsync(
                        exchange: exchangeName, type: ExchangeType.Direct, durable: true,
                        cancellationToken: cancellationToken);
                    _declaredExchanges.Add(exchangeName);
                }

                await _publisherChannel.BasicPublishAsync(
                    exchange: exchangeName, routingKey: routingKey, mandatory: true,
                    basicProperties: properties, body: body, cancellationToken: cancellationToken);
            }
            catch
            {
                // A failed or cancelled confirm has an uncertain outcome. Let the durable
                // outbox retry the same MessageId on a fresh channel; never hide this failure.
                try { await ResetPublisherChannelAsync(); }
                catch { /* Preserve the original publication exception. */ }
                throw;
            }
            finally { _publisherLock.Release(); }
        }
        catch (Exception exception)
        {
            MessageBusTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    public async Task<IAsyncDisposable> SubscribeAsync<T>(
        string queueName,
        string exchangeName,
        string routingKey,
        Func<MessageEnvelope<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        ArgumentNullException.ThrowIfNull(handler);

        var consumer = _configuration.GetSection($"Messaging:Consumers:{queueName}");
        var concurrency = ReadBoundedSetting(consumer, "Concurrency", 1, 32);
        var prefetch = ReadBoundedSetting(consumer, "Prefetch", concurrency == 1 ? 0 : concurrency * 4, 1024);
        if (concurrency > 1 && prefetch < concurrency)
            throw new InvalidOperationException($"Messaging:Consumers:{queueName}:Prefetch must be at least Concurrency.");

        var connection = await GetConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false, consumerDispatchConcurrency: (ushort)concurrency),
            cancellationToken: cancellationToken);
        var acknowledgementGate = concurrency > 1 ? new SemaphoreSlim(1, 1) : null;

        try
        {
            await DeclareQueueAsync(channel, queueName, exchangeName, routingKey, cancellationToken);
            if (prefetch > 0)
                await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: (ushort)prefetch,
                    global: false, cancellationToken: cancellationToken);

            var eventConsumer = new AsyncEventingBasicConsumer(channel);
            eventConsumer.ReceivedAsync += (_, eventArgs) => HandleMessageAsync(
                channel, queueName, eventArgs, handler, cancellationToken, acknowledgementGate);

            var consumerTag = await channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: false,
                consumer: eventConsumer,
                cancellationToken: cancellationToken);

            return new RabbitMqSubscription(channel, consumerTag, acknowledgementGate);
        }
        catch
        {
            await channel.DisposeAsync();
            acknowledgementGate?.Dispose();
            throw;
        }
    }

    internal static async Task DeclareQueueAsync(
        IChannel channel, string queueName, string exchangeName, string routingKey,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: exchangeName, type: ExchangeType.Direct, durable: true,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            queue: queueName, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            queue: queueName, exchange: exchangeName, routingKey: routingKey,
            cancellationToken: cancellationToken);
    }

    internal static async Task HandleMessageAsync<T>(
        IChannel channel,
        string queueName,
        BasicDeliverEventArgs eventArgs,
        Func<MessageEnvelope<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken,
        SemaphoreSlim? acknowledgementGate = null)
    {
        // A delivery must inherit its message's context, never the subscription's activity.
        var previousActivity = Activity.Current;
        Activity.Current = null;
        try
        {
            using var activity = MessageBusTelemetry.StartConsumerActivity(queueName, eventArgs);
            try
            {
                var message = JsonSerializer.Deserialize<MessageEnvelope<T>>(
                    eventArgs.Body.Span, SerializerOptions)
                    ?? throw new InvalidOperationException(
                        $"RabbitMQ message for '{typeof(T).FullName}' was empty or invalid.");

                activity?.SetTag("messaging.message.id", message.Id.ToString());
                await handler(message, cancellationToken);
                await CompleteDeliveryAsync(channel, eventArgs.DeliveryTag, acknowledge: true,
                    cancellationToken, acknowledgementGate);
            }
            catch (Exception exception)
            {
                MessageBusTelemetry.RecordException(activity, exception);
                // Failed consumers must release the delivery for retry, rather than leave it unacknowledged until reconnect.
                await CompleteDeliveryAsync(channel, eventArgs.DeliveryTag, acknowledge: false,
                    cancellationToken, acknowledgementGate);
                throw;
            }
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }

    private static async Task CompleteDeliveryAsync(IChannel channel, ulong deliveryTag, bool acknowledge,
        CancellationToken cancellationToken, SemaphoreSlim? acknowledgementGate)
    {
        // Concurrent handlers own separate scopes, but acknowledgements still share a
        // channel. Serialize only their frames and never acknowledge multiple deliveries.
        if (acknowledgementGate is not null) await acknowledgementGate.WaitAsync(cancellationToken);
        try
        {
            if (acknowledge) await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
            else await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true, cancellationToken);
        }
        finally { acknowledgementGate?.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _publisherLock.WaitAsync();
        try
        {
            await _connectionLock.WaitAsync();
            try
            {
                try { await ResetPublisherChannelAsync(); }
                finally
                {
                    if (_connection is not null) await _connection.DisposeAsync();
                    _connection = null;
                }
            }
            finally { _connectionLock.Release(); }
        }
        finally { _publisherLock.Release(); }
        // Keep the gates available for calls already waiting during shutdown. They wake,
        // see _disposed and fail instead of racing a disposed semaphore.
    }

    private async ValueTask ResetPublisherChannelAsync()
    {
        var channel = _publisherChannel;
        _publisherChannel = null;
        _declaredExchanges.Clear();
        if (channel is not null) await channel.DisposeAsync();
    }

    private static int ReadBoundedSetting(IConfigurationSection section, string key, int fallback, int maximum)
    {
        var configured = section[key];
        if (configured is null) return fallback;
        if (!int.TryParse(configured, out var value) || value < (key == "Concurrency" ? 1 : 0) || value > maximum)
            throw new InvalidOperationException($"{section.Path}:{key} must be between {(key == "Concurrency" ? 1 : 0)} and {maximum}.");
        return value;
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_connection is not null)
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_connection is null)
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(_connectionString)
                };

                _connection = await factory.CreateConnectionAsync(cancellationToken);
            }

            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private sealed class RabbitMqSubscription(IChannel channel, string consumerTag, SemaphoreSlim? acknowledgementGate)
        : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                if (channel.IsOpen)
                {
                    if (acknowledgementGate is not null) await acknowledgementGate.WaitAsync();
                    try { await channel.BasicCancelAsync(consumerTag); }
                    finally { acknowledgementGate?.Release(); }
                }
            }
            finally
            {
                // Closing the channel also releases outstanding deliveries when cancelling
                // the consumer fails, so they can be retried by another subscription.
                try { await channel.DisposeAsync(); }
                finally { acknowledgementGate?.Dispose(); }
            }
        }
    }
}
