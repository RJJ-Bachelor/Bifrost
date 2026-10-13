using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Shared.Infrastructure.Persistence.Messaging;

// Prepare a durable destination while its message handler is still being developed.
// Messages remain in RabbitMQ until a consumer is added; none are acknowledged here.
internal sealed class RabbitMqQueueInitializer(
    IConfiguration configuration, ILogger<RabbitMqQueueInitializer> logger,
    string queueName, string exchangeName, string routingKey) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException("The 'rabbitmq' connection string is not configured.");
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await RabbitMqMessageBus.DeclareQueueAsync(channel, queueName, exchangeName, routingKey, cancellationToken);

        logger.LogInformation("Prepared durable queue {QueueName} for {ExchangeName}/{RoutingKey}; messages await a consumer.",
            queueName, exchangeName, routingKey);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
