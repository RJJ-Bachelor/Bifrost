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
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqMessageBus(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException(
                "The 'rabbitmq' connection string is not configured.");
    }

    public async Task PublishAsync(
        string id,
        object payload,
        string exchangeName,
        string routingKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);

        var connection = await GetConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Direct,
            durable: true,
            cancellationToken: cancellationToken);

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
            Type = payload.GetType().FullName
        };

        await channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
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

        var connection = await GetConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(
            cancellationToken: cancellationToken);

        try
        {
            await channel.ExchangeDeclareAsync(
                exchange: exchangeName,
                type: ExchangeType.Direct,
                durable: true,
                cancellationToken: cancellationToken);
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);
            await channel.QueueBindAsync(
                queue: queueName,
                exchange: exchangeName,
                routingKey: routingKey,
                cancellationToken: cancellationToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, eventArgs) =>
            {
                var message = JsonSerializer.Deserialize<MessageEnvelope<T>>(
                    eventArgs.Body.Span,
                    SerializerOptions);

                if (message is null)
                {
                    throw new InvalidOperationException(
                        $"RabbitMQ message for '{typeof(T).FullName}' was empty or invalid.");
                }

                await handler(message, cancellationToken);
                await channel.BasicAckAsync(
                    deliveryTag: eventArgs.DeliveryTag,
                    multiple: false,
                    cancellationToken: cancellationToken);
            };

            var consumerTag = await channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken);

            return new RabbitMqSubscription(channel, consumerTag);
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connectionLock.Dispose();
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
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

    private sealed class RabbitMqSubscription(IChannel channel, string consumerTag)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (channel.IsOpen)
            {
                await channel.BasicCancelAsync(consumerTag);
            }

            await channel.DisposeAsync();
        }
    }
}
