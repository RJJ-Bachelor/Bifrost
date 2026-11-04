using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;

namespace Shared.Test;

public sealed class MessagingContractTests
{
    [Fact]
    public void MissingBrokerConfigurationFailsWithAnActionableError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new RabbitMqMessageBus(new ConfigurationBuilder().Build()));

        Assert.Contains("rabbitmq", error.Message);
    }

    [Fact]
    public async Task InvalidPublishArgumentsAreRejectedBeforeOpeningABrokerConnection()
    {
        await using var bus = Bus();
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => bus.PublishAsync(Guid.Empty, "payload", "exchange", "route"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => bus.PublishAsync(id, null!, "exchange", "route"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => bus.PublishAsync(id, "payload", " ", "route"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => bus.PublishAsync(id, "payload", "exchange", ""));
    }

    [Fact]
    public async Task InvalidSubscriptionArgumentsAreRejectedBeforeOpeningABrokerConnection()
    {
        await using var bus = Bus();
        static Task Handler(MessageEnvelope<string> _, CancellationToken token) => Task.CompletedTask;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => bus.SubscribeAsync<string>(" ", "exchange", "route", Handler));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => bus.SubscribeAsync<string>("queue", "", "route", Handler));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => bus.SubscribeAsync<string>("queue", "exchange", " ", Handler));
        await Assert.ThrowsAsync<ArgumentNullException>(() => bus.SubscribeAsync<string>("queue", "exchange", "route", null!));
    }

    [Fact]
    public async Task CanceledPublishAndSubscriptionDoNotAttemptToConnect()
    {
        await using var bus = Bus();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bus.PublishAsync(Guid.NewGuid(), "payload", "exchange", "route", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bus.SubscribeAsync<string>("queue", "exchange", "route", (_, _) => Task.CompletedTask, cancellation.Token));
    }

    [Fact]
    public async Task QueueTopologyIsDurableAndBoundBeforeAConsumerCanSubscribe()
    {
        var channel = DispatchProxy.Create<IChannel, TopologyChannel>();
        using var cancellation = new CancellationTokenSource();

        await RabbitMqMessageBus.DeclareQueueAsync(channel, "queue", "exchange", "route", cancellation.Token);

        var calls = ((TopologyChannel)(object)channel).Calls;
        Assert.Equal(new[] { "ExchangeDeclareAsync", "QueueDeclareAsync", "QueueBindAsync" }, calls.Select(call => call.Name));
        Assert.Equal("exchange", calls[0].Arguments["exchange"]);
        Assert.Equal(ExchangeType.Direct, calls[0].Arguments["type"]);
        Assert.Equal(true, calls[0].Arguments["durable"]);
        Assert.Equal("queue", calls[1].Arguments["queue"]);
        Assert.Equal(true, calls[1].Arguments["durable"]);
        Assert.Equal(false, calls[1].Arguments["exclusive"]);
        Assert.Equal(false, calls[1].Arguments["autoDelete"]);
        Assert.Equal("queue", calls[2].Arguments["queue"]);
        Assert.Equal("exchange", calls[2].Arguments["exchange"]);
        Assert.Equal("route", calls[2].Arguments["routingKey"]);
        Assert.All(calls, call => Assert.Equal(cancellation.Token, call.Arguments["cancellationToken"]));
    }

    [Fact]
    public async Task MessagingRegistrationPreservesAnExistingBusAndValidatesQueueNames()
    {
        var services = new ServiceCollection();
        var bus = Bus();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IMessageBus>(bus);

        services.AddRabbitMqMessageBus(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IMessageBus));
        Assert.ThrowsAny<ArgumentException>(() => services.AddRabbitMqQueue("", "exchange", "route"));
        Assert.ThrowsAny<ArgumentException>(() => services.AddRabbitMqQueue("queue", " ", "route"));
        Assert.ThrowsAny<ArgumentException>(() => services.AddRabbitMqQueue("queue", "exchange", ""));
        await bus.DisposeAsync();
    }

    [Fact]
    public async Task QueueInitializerRequiresConfigurationAndHasANoOpShutdown()
    {
        var worker = new RabbitMqQueueInitializer(new ConfigurationBuilder().Build(),
            NullLogger<RabbitMqQueueInitializer>.Instance, "queue", "exchange", "route");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.StartAsync(default));

        Assert.Contains("rabbitmq", error.Message);
        await worker.StopAsync(default);
    }

    private static RabbitMqMessageBus Bus() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:rabbitmq"] = "amqp://unused" }).Build());

    public sealed record ChannelCall(string Name, Dictionary<string, object?> Arguments);

    public class TopologyChannel : DispatchProxy
    {
        public List<ChannelCall> Calls { get; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            var parameters = targetMethod.GetParameters();
            Calls.Add(new(targetMethod.Name, parameters.Select((parameter, index) => (parameter.Name!, Value: args![index]))
                .ToDictionary(item => item.Item1, item => item.Value)));
            return targetMethod.Name switch
            {
                nameof(IChannel.QueueDeclareAsync) => Task.FromResult(new QueueDeclareOk("queue", 0, 0)),
                nameof(IChannel.ExchangeDeclareAsync) or nameof(IChannel.QueueBindAsync) => Task.CompletedTask,
                _ => throw new InvalidOperationException("Unexpected channel operation: " + targetMethod.Name)
            };
        }
    }
}
