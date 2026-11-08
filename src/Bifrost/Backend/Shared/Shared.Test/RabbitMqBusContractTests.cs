using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Messaging;

namespace Shared.Test;

// Activity listeners are process-wide. Keep these tests separate from the existing trace tests.
[CollectionDefinition("RabbitMQ transport contracts", DisableParallelization = true)]
public sealed class RabbitMqTransportCollection;

[Collection("RabbitMQ transport contracts")]
public sealed class RabbitMqBusContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("contracts.notification.v2")]
    public async Task PublishWritesADurableJsonEnvelopeWithConfirmationsAndTraceContext(string? explicitType)
    {
        await using var transport = new TransportDouble();
        var channel = transport.AddChannel();
        var completed = new List<Activity>();
        using var listener = Listen(completed);
        using var request = new Activity("HTTP request").SetIdFormat(ActivityIdFormat.W3C).Start();
        request.AddBaggage("workflow", "notification");
        request.TraceStateString = "vendor=state";
        using var cancellation = new CancellationTokenSource();
        var id = Guid.NewGuid();
        var payload = new NotificationPayload("Answer", 3);

        await transport.Bus.PublishAsync(id, payload, "notifications", "notification.created", cancellation.Token, explicitType);

        var publication = Assert.Single(channel.Publications);
        Assert.Equal("notifications", publication.Exchange);
        Assert.Equal("notification.created", publication.Route);
        Assert.True(publication.Mandatory);
        Assert.Equal(cancellation.Token, publication.Token);
        Assert.Equal("application/json", publication.Properties.ContentType);
        Assert.Equal(DeliveryModes.Persistent, publication.Properties.DeliveryMode);
        Assert.Equal(id.ToString(), publication.Properties.MessageId);
        Assert.Equal(explicitType ?? typeof(NotificationPayload).FullName, publication.Properties.Type);
        var envelope = JsonSerializer.Deserialize<MessageEnvelope<NotificationPayload>>(publication.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(new MessageEnvelope<NotificationPayload>(id, payload), envelope);
        using var json = JsonDocument.Parse(publication.Body);
        Assert.Equal("Answer", json.RootElement.GetProperty("payload").GetProperty("message").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("payload").GetProperty("generation").GetInt32());
        var activity = Assert.Single(completed);
        Assert.Equal(ActivityKind.Producer, activity.Kind);
        Assert.Equal(request.TraceId, activity.TraceId);
        Assert.Equal(request.SpanId, activity.ParentSpanId);
        Assert.Equal(activity.Id, Header(publication.Properties, "traceparent"));
        Assert.Equal("vendor=state", Header(publication.Properties, "tracestate"));
        Assert.Equal(new[] { "workflow", "notification" },
            Header(publication.Properties, "baggage").Split('=', 2, StringSplitOptions.TrimEntries));
        Assert.Equal(publication.Body.Length, activity.GetTagItem("messaging.message.body.size"));
        Assert.Same(request, Activity.Current);
        var created = Assert.Single(transport.Connection.CreatedChannels);
        Assert.True(created.Options!.PublisherConfirmationsEnabled);
        Assert.True(created.Options.PublisherConfirmationTrackingEnabled);
        Assert.Equal(cancellation.Token, created.Token);
        Assert.Equal(new[] { "ExchangeDeclareAsync", "BasicPublishAsync" }, channel.Calls);
        Assert.False(channel.Disposed);
        Assert.False(transport.Connection.Disposed);
    }

    [Theory]
    [InlineData("ExchangeDeclareAsync")]
    [InlineData("BasicPublishAsync")]
    public async Task FailedPublishDisposesTheChannelAndReportsTheOriginalFailure(string operation)
    {
        await using var transport = new TransportDouble();
        var channel = transport.AddChannel();
        var failure = new IOException("Broker rejected publication");
        channel.FailureOperation = operation;
        channel.Failure = failure;
        var completed = new List<Activity>();
        using var listener = Listen(completed);

        var thrown = await Assert.ThrowsAsync<IOException>(() =>
            transport.Bus.PublishAsync(Guid.NewGuid(), new NotificationPayload("Answer", 3), "notifications", "notification.created"));

        Assert.Same(failure, thrown);
        Assert.True(channel.Disposed);
        Assert.Equal("DisposeAsync", channel.Calls.Last());
        var activity = Assert.Single(completed);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(IOException).FullName, activity.GetTagItem("error.type"));
        Assert.Contains(activity.Events, item => item.Name == "exception");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubscriptionDeliversTheEnvelopeWithManualAcknowledgementAndReleasesItsChannel(bool channelOpenAtShutdown)
    {
        await using var transport = new TransportDouble();
        var channel = transport.AddChannel();
        using var cancellation = new CancellationTokenSource();
        var expected = new MessageEnvelope<NotificationPayload>(Guid.NewGuid(), new("Answer", 3));
        var deliveries = new List<MessageEnvelope<NotificationPayload>>();
        var subscription = await transport.Bus.SubscribeAsync<NotificationPayload>("gna", "notifications", "notification.created",
            (message, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                Assert.DoesNotContain("BasicAckAsync", channel.Calls);
                deliveries.Add(message);
                return Task.CompletedTask;
            }, cancellation.Token);

        await channel.Consumer!.HandleBasicDeliverAsync("consumer-1", 42, false, "notifications", "notification.created",
            new BasicProperties(), JsonSerializer.SerializeToUtf8Bytes(expected, new JsonSerializerOptions(JsonSerializerDefaults.Web)), cancellation.Token);
        channel.Open = channelOpenAtShutdown;
        await subscription.DisposeAsync();

        Assert.Equal(expected, Assert.Single(deliveries));
        Assert.Equal(new[] { "ExchangeDeclareAsync", "QueueDeclareAsync", "QueueBindAsync", "BasicConsumeAsync" }, channel.Calls.Take(4));
        Assert.Equal("gna", channel.ConsumedQueue);
        Assert.False(channel.AutoAck);
        Assert.Equal(cancellation.Token, channel.SubscriptionToken);
        Assert.Equal(42UL, channel.AcknowledgedDeliveryTag);
        Assert.Equal(cancellation.Token, channel.AcknowledgementToken);
        Assert.False(channel.AcknowledgeMultiple);
        Assert.Null(channel.RejectedDeliveryTag);
        Assert.Equal(channelOpenAtShutdown ? "consumer-1" : null, channel.CanceledConsumerTag);
        Assert.True(channel.Disposed);
        Assert.Equal("DisposeAsync", channel.Calls.Last());
    }

    [Theory]
    [InlineData("ExchangeDeclareAsync")]
    [InlineData("QueueDeclareAsync")]
    [InlineData("QueueBindAsync")]
    [InlineData("BasicConsumeAsync")]
    public async Task SubscriptionSetupFailureReleasesTheChannel(string operation)
    {
        await using var transport = new TransportDouble();
        var channel = transport.AddChannel();
        var failure = new IOException("Subscription setup failed");
        channel.FailureOperation = operation;
        channel.Failure = failure;

        var thrown = await Assert.ThrowsAsync<IOException>(() => transport.Bus.SubscribeAsync<NotificationPayload>(
            "gna", "notifications", "notification.created", (_, _) => Task.CompletedTask));

        Assert.Same(failure, thrown);
        Assert.True(channel.Disposed);
        Assert.Equal("DisposeAsync", channel.Calls.Last());
        Assert.Null(channel.CanceledConsumerTag);
    }

    [Fact]
    public async Task ConsumerFailureIsReturnedToTheTransportAndRequeuesTheDelivery()
    {
        await using var transport = new TransportDouble();
        var channel = transport.AddChannel();
        var failure = new IOException("Saving notification failed");
        await using var subscription = await transport.Bus.SubscribeAsync<NotificationPayload>(
            "gna", "notifications", "notification.created", (_, _) => Task.FromException(failure));
        var message = new MessageEnvelope<NotificationPayload>(Guid.NewGuid(), new("Answer", 3));

        var thrown = await Assert.ThrowsAsync<IOException>(() => channel.Consumer!.HandleBasicDeliverAsync(
            "consumer-1", 42, true, "notifications", "notification.created", new BasicProperties(),
            JsonSerializer.SerializeToUtf8Bytes(message, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        Assert.Same(failure, thrown);
        Assert.Null(channel.AcknowledgedDeliveryTag);
        Assert.Equal(42UL, channel.RejectedDeliveryTag);
        Assert.True(channel.Requeue);
        Assert.False(channel.RejectMultiple);
    }

    [Fact]
    public async Task PublicationsReuseAConfirmedChannelAndDeclareEachExchangeOnceUntilDisposal()
    {
        var transport = new TransportDouble();
        var first = transport.AddChannel();
        await using (transport)
        {
            await transport.Bus.PublishAsync(Guid.NewGuid(), "first", "notifications", "created");
            await transport.Bus.PublishAsync(Guid.NewGuid(), "second", "notifications", "created");
            await transport.Bus.PublishAsync(Guid.NewGuid(), "third", "requests", "updated");

            Assert.Single(transport.Connection.CreatedChannels);
            Assert.Equal(3, first.Publications.Count);
            Assert.Equal(2, first.Calls.Count(call => call == "ExchangeDeclareAsync"));
            Assert.All(first.Publications, publication => Assert.True(publication.Mandatory));
            Assert.False(first.Disposed);
            Assert.False(transport.Connection.Disposed);
        }

        Assert.True(first.Disposed);
        Assert.True(transport.Connection.Disposed);
    }

    [Fact]
    public async Task ConcurrentPublicationsWaitForThePreviousConfirmation()
    {
        await using var transport = new TransportDouble();
        var channel = transport.AddChannel();
        channel.PublishStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ReleasePublication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = transport.Bus.PublishAsync(Guid.NewGuid(), "first", "notifications", "created");
        await channel.PublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = transport.Bus.PublishAsync(Guid.NewGuid(), "second", "notifications", "created");

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Single(channel.Publications);
        channel.ReleasePublication.SetResult();
        await Task.WhenAll(first, second);

        Assert.Single(transport.Connection.CreatedChannels);
        Assert.Equal(2, channel.Publications.Count);
        Assert.Equal(1, channel.MaximumConcurrentPublications);
    }

    [Fact]
    public async Task CancellingAPublisherWaitingForTheChannelDoesNotInterruptAnInFlightConfirmation()
    {
        await using var transport = new TransportDouble();
        var channel = transport.AddChannel();
        channel.PublishStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ReleasePublication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = transport.Bus.PublishAsync(Guid.NewGuid(), "first", "notifications", "created");
        await channel.PublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var second = transport.Bus.PublishAsync(Guid.NewGuid(), "second", "notifications", "created", cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(channel.Disposed);
        channel.ReleasePublication.SetResult();
        await first;
        Assert.Single(channel.Publications);
    }

    [Fact]
    public async Task CancellingAnInFlightConfirmationReleasesTheChannelForAnExplicitRetry()
    {
        await using var transport = new TransportDouble();
        var canceled = transport.AddChannel();
        var replacement = transport.AddChannel();
        canceled.PublishStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        canceled.ReleasePublication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var publication = transport.Bus.PublishAsync(id, "answer", "notifications", "created", cancellation.Token);
        await canceled.PublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publication);
        await transport.Bus.PublishAsync(id, "answer", "notifications", "created");

        Assert.True(canceled.Disposed);
        Assert.False(replacement.Disposed);
        Assert.Equal(id.ToString(), Assert.Single(canceled.Publications).Properties.MessageId);
        Assert.Equal(id.ToString(), Assert.Single(replacement.Publications).Properties.MessageId);
        Assert.Equal(2, transport.Connection.CreatedChannels.Count);
    }

    [Fact]
    public async Task FailedConfirmationReplacesTheChannelAndKeepsTheMessageIdForAnExplicitRetry()
    {
        await using var transport = new TransportDouble();
        var failed = transport.AddChannel();
        failed.FailureOperation = "BasicPublishAsync";
        failed.Failure = new IOException("Confirmation failed");
        var replacement = transport.AddChannel();
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<IOException>(() => transport.Bus.PublishAsync(id, "answer", "notifications", "created"));
        await transport.Bus.PublishAsync(id, "answer", "notifications", "created");

        Assert.True(failed.Disposed);
        Assert.False(replacement.Disposed);
        Assert.Equal(id.ToString(), Assert.Single(replacement.Publications).Properties.MessageId);
        Assert.Equal(2, transport.Connection.CreatedChannels.Count);
        Assert.Equal(1, replacement.Calls.Count(call => call == "ExchangeDeclareAsync"));
        Assert.All(transport.Connection.CreatedChannels, creation =>
        {
            Assert.True(creation.Options!.PublisherConfirmationsEnabled);
            Assert.True(creation.Options.PublisherConfirmationTrackingEnabled);
        });
    }

    [Fact]
    public async Task AClosedPublisherChannelIsReplacedAndItsExchangesAreDeclaredAgain()
    {
        await using var transport = new TransportDouble();
        var first = transport.AddChannel();
        var replacement = transport.AddChannel();
        await transport.Bus.PublishAsync(Guid.NewGuid(), "first", "notifications", "created");
        first.Open = false;

        await transport.Bus.PublishAsync(Guid.NewGuid(), "second", "notifications", "created");

        Assert.True(first.Disposed);
        Assert.Single(replacement.Publications);
        Assert.Equal(1, replacement.Calls.Count(call => call == "ExchangeDeclareAsync"));
    }

    [Fact]
    public async Task ShutdownWaitsForAnInFlightConfirmationAndRejectsFurtherPublications()
    {
        var transport = new TransportDouble();
        var channel = transport.AddChannel();
        channel.PublishStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ReleasePublication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var publication = transport.Bus.PublishAsync(Guid.NewGuid(), "answer", "notifications", "created");
        await channel.PublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposal = transport.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        channel.ReleasePublication.SetResult();
        await publication;
        await disposal;

        Assert.True(channel.Disposed);
        Assert.True(transport.Connection.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            transport.Bus.PublishAsync(Guid.NewGuid(), "next", "notifications", "created"));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task ConsumerConcurrencyAndPrefetchAreScopedToTheConfiguredQueue()
    {
        await using var transport = new TransportDouble(new Dictionary<string, string?>
        {
            ["Messaging:Consumers:gna-notification-requested:Concurrency"] = "8",
            ["Messaging:Consumers:gna-notification-requested:Prefetch"] = "32"
        });
        var notifications = transport.AddChannel();
        var ordered = transport.AddChannel();

        await using var parallel = await transport.Bus.SubscribeAsync<string>("gna-notification-requested", "notifications", "created", (_, _) => Task.CompletedTask);
        await using var sequential = await transport.Bus.SubscribeAsync<string>("ordered-requests", "requests", "updated", (_, _) => Task.CompletedTask);

        Assert.Equal((ushort)8, transport.Connection.CreatedChannels[0].Options!.ConsumerDispatchConcurrency);
        Assert.False(transport.Connection.CreatedChannels[0].Options!.PublisherConfirmationsEnabled);
        Assert.False(transport.Connection.CreatedChannels[0].Options!.PublisherConfirmationTrackingEnabled);
        Assert.Equal((ushort)32, notifications.PrefetchCount);
        Assert.False(notifications.GlobalPrefetch);
        Assert.Equal((ushort)1, transport.Connection.CreatedChannels[1].Options!.ConsumerDispatchConcurrency);
        Assert.Null(ordered.PrefetchCount);
    }

    [Fact]
    public async Task ConcurrentConsumerCompletionsSerializeIndividualAcknowledgementsAndRejections()
    {
        await using var transport = new TransportDouble(new Dictionary<string, string?>
        {
            ["Messaging:Consumers:gna:Concurrency"] = "2"
        });
        var channel = transport.AddChannel();
        channel.CompletionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ReleaseCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("Saving notification failed");
        await using var subscription = await transport.Bus.SubscribeAsync<string>("gna", "notifications", "created",
            (message, _) => message.Payload == "fail" ? Task.FromException(failure) : Task.CompletedTask);
        var completed = channel.Consumer!.HandleBasicDeliverAsync("consumer-1", 42, false, "notifications", "created",
            new BasicProperties(), JsonSerializer.SerializeToUtf8Bytes(new MessageEnvelope<string>(Guid.NewGuid(), "ok"),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await channel.CompletionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var failed = channel.Consumer!.HandleBasicDeliverAsync("consumer-1", 43, false, "notifications", "created",
            new BasicProperties(), JsonSerializer.SerializeToUtf8Bytes(new MessageEnvelope<string>(Guid.NewGuid(), "fail"),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.False(completed.IsCompleted);
        Assert.False(failed.IsCompleted);
        Assert.Single(channel.Completions);
        channel.ReleaseCompletion.SetResult();
        await completed;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => failed));

        Assert.Equal(1, channel.MaximumConcurrentCompletions);
        Assert.Collection(channel.Completions,
            completion => Assert.Equal((42UL, true, false, false), completion),
            completion => Assert.Equal((43UL, false, false, true), completion));
    }

    [Fact]
    public async Task FailedConsumerCancellationStillClosesTheChannelToReleaseOutstandingDeliveries()
    {
        await using var transport = new TransportDouble(new Dictionary<string, string?>
        {
            ["Messaging:Consumers:gna:Concurrency"] = "2"
        });
        var channel = transport.AddChannel();
        var subscription = await transport.Bus.SubscribeAsync<string>("gna", "notifications", "created", (_, _) => Task.CompletedTask);
        var failure = new IOException("Consumer cancellation failed");
        channel.FailureOperation = "BasicCancelAsync";
        channel.Failure = failure;

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => subscription.DisposeAsync().AsTask()));

        Assert.True(channel.Disposed);
        Assert.Equal("DisposeAsync", channel.Calls.Last());
        await subscription.DisposeAsync();
        Assert.Equal(1, channel.Calls.Count(call => call == "DisposeAsync"));
    }

    [Theory]
    [InlineData("Concurrency", "0")]
    [InlineData("Concurrency", "33")]
    [InlineData("Concurrency", "invalid")]
    [InlineData("Prefetch", "-1")]
    [InlineData("Prefetch", "1025")]
    public async Task InvalidConsumerSettingsFailBeforeOpeningAChannel(string key, string value)
    {
        await using var transport = new TransportDouble(new Dictionary<string, string?>
        {
            [$"Messaging:Consumers:gna:{key}"] = value
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.Bus.SubscribeAsync<string>("gna", "notifications", "created", (_, _) => Task.CompletedTask));

        Assert.Contains($"Messaging:Consumers:gna:{key}", error.Message);
        Assert.Empty(transport.Connection.CreatedChannels);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    public async Task ConcurrentConsumersRequireABoundedPrefetchAtLeastAsLargeAsTheirConcurrency(string prefetch)
    {
        await using var transport = new TransportDouble(new Dictionary<string, string?>
        {
            ["Messaging:Consumers:gna:Concurrency"] = "8",
            ["Messaging:Consumers:gna:Prefetch"] = prefetch
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.Bus.SubscribeAsync<string>("gna", "notifications", "created", (_, _) => Task.CompletedTask));
        Assert.Empty(transport.Connection.CreatedChannels);
    }

    private static ActivityListener Listen(List<Activity> completed)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessageBusTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = completed.Add
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static string Header(BasicProperties properties, string name) =>
        Encoding.UTF8.GetString(Assert.IsType<byte[]>(properties.Headers![name]));

    public sealed record NotificationPayload(string Message, int Generation);
    public sealed record ChannelCreation(CreateChannelOptions? Options, CancellationToken Token);
    public sealed record Publication(string Exchange, string Route, bool Mandatory, BasicProperties Properties, byte[] Body, CancellationToken Token);

    private sealed class TransportDouble : IAsyncDisposable
    {
        public RabbitMqMessageBus Bus { get; }
        public ConnectionDouble Connection { get; }

        public TransportDouble(Dictionary<string, string?>? settings = null)
        {
            // The sealed bus has no connection-factory seam. Seed its cached interface with a transport
            // double; malformed configuration also guarantees these unit tests cannot open a network connection.
            settings ??= [];
            settings["ConnectionStrings:rabbitmq"] = "not-a-real-connection-string";
            Bus = new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            var connection = DispatchProxy.Create<IConnection, ConnectionDouble>();
            Connection = (ConnectionDouble)(object)connection;
            typeof(RabbitMqMessageBus).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Bus, connection);
        }

        public ChannelDouble AddChannel()
        {
            var channel = DispatchProxy.Create<IChannel, ChannelDouble>();
            Connection.PendingChannels.Enqueue(channel);
            return (ChannelDouble)(object)channel;
        }

        public ValueTask DisposeAsync() => Bus.DisposeAsync();
    }

    public class ConnectionDouble : DispatchProxy
    {
        public Queue<IChannel> PendingChannels { get; } = new();
        public List<ChannelCreation> CreatedChannels { get; } = [];
        public bool Disposed { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod!.Name switch
            {
                nameof(IConnection.CreateChannelAsync) => CreateChannel((CreateChannelOptions?)args![0], (CancellationToken)args[1]!),
                nameof(IAsyncDisposable.DisposeAsync) => DisposeConnection(),
                _ => throw new InvalidOperationException("Unexpected connection operation: " + targetMethod.Name)
            };
        }

        private Task<IChannel> CreateChannel(CreateChannelOptions? options, CancellationToken token)
        {
            CreatedChannels.Add(new(options, token));
            return Task.FromResult(PendingChannels.Dequeue());
        }

        private ValueTask DisposeConnection()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    public class ChannelDouble : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public List<Publication> Publications { get; } = [];
        public bool Disposed { get; private set; }
        public bool Open { get; set; } = true;
        public string? FailureOperation { get; set; }
        public Exception? Failure { get; set; }
        public IAsyncBasicConsumer? Consumer { get; private set; }
        public string? ConsumedQueue { get; private set; }
        public bool AutoAck { get; private set; }
        public CancellationToken SubscriptionToken { get; private set; }
        public ulong? AcknowledgedDeliveryTag { get; private set; }
        public CancellationToken AcknowledgementToken { get; private set; }
        public bool AcknowledgeMultiple { get; private set; }
        public ulong? RejectedDeliveryTag { get; private set; }
        public bool RejectMultiple { get; private set; }
        public bool Requeue { get; private set; }
        public string? CanceledConsumerTag { get; private set; }
        public ushort? PrefetchCount { get; private set; }
        public bool GlobalPrefetch { get; private set; }
        public TaskCompletionSource? PublishStarted { get; set; }
        public TaskCompletionSource? ReleasePublication { get; set; }
        public int MaximumConcurrentPublications { get; private set; }
        private int activePublications;
        public TaskCompletionSource? CompletionStarted { get; set; }
        public TaskCompletionSource? ReleaseCompletion { get; set; }
        public List<(ulong DeliveryTag, bool Acknowledge, bool Multiple, bool Requeue)> Completions { get; } = [];
        public int MaximumConcurrentCompletions { get; private set; }
        private int activeCompletions;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod!;
            Calls.Add(method.Name);
            var parameters = method.GetParameters();
            var arguments = parameters.Select((parameter, index) => (Name: parameter.Name!, Value: args![index]))
                .ToDictionary(item => item.Name, item => item.Value);
            if (method.Name == FailureOperation) return FailedOperation(method.ReturnType, Failure!);

            switch (method.Name)
            {
                case nameof(IChannel.ExchangeDeclareAsync):
                case nameof(IChannel.QueueBindAsync):
                    return Task.CompletedTask;
                case nameof(IChannel.QueueDeclareAsync):
                    return Task.FromResult(new QueueDeclareOk("gna", 0, 0));
                case nameof(IChannel.BasicPublishAsync):
                    return PublishAsync(arguments);
                case nameof(IChannel.BasicQosAsync):
                    PrefetchCount = (ushort)arguments["prefetchCount"]!;
                    GlobalPrefetch = (bool)arguments["global"]!;
                    return Task.CompletedTask;
                case nameof(IChannel.BasicConsumeAsync):
                    Consumer = (IAsyncBasicConsumer)arguments["consumer"]!;
                    ConsumedQueue = (string)arguments["queue"]!;
                    AutoAck = (bool)arguments["autoAck"]!;
                    SubscriptionToken = (CancellationToken)arguments["cancellationToken"]!;
                    return Task.FromResult("consumer-1");
                case nameof(IChannel.BasicAckAsync):
                    AcknowledgedDeliveryTag = (ulong)arguments["deliveryTag"]!;
                    AcknowledgementToken = (CancellationToken)arguments["cancellationToken"]!;
                    AcknowledgeMultiple = (bool)arguments["multiple"]!;
                    return CompleteDeliveryAsync(arguments, acknowledge: true);
                case nameof(IChannel.BasicNackAsync):
                    RejectedDeliveryTag = (ulong)arguments["deliveryTag"]!;
                    RejectMultiple = (bool)arguments["multiple"]!;
                    Requeue = (bool)arguments["requeue"]!;
                    return CompleteDeliveryAsync(arguments, acknowledge: false);
                case nameof(IChannel.BasicCancelAsync):
                    CanceledConsumerTag = (string)arguments["consumerTag"]!;
                    return Task.CompletedTask;
                case "get_IsOpen":
                    return Open;
                case nameof(IAsyncDisposable.DisposeAsync):
                    Disposed = true;
                    return ValueTask.CompletedTask;
                default:
                    throw new InvalidOperationException("Unexpected channel operation: " + method.Name);
            }
        }

        private async ValueTask PublishAsync(Dictionary<string, object?> arguments)
        {
            var active = Interlocked.Increment(ref activePublications);
            MaximumConcurrentPublications = Math.Max(MaximumConcurrentPublications, active);
            var token = (CancellationToken)arguments["cancellationToken"]!;
            var publication = new Publication((string)arguments["exchange"]!, (string)arguments["routingKey"]!,
                (bool)arguments["mandatory"]!, (BasicProperties)arguments["basicProperties"]!,
                ((ReadOnlyMemory<byte>)arguments["body"]!).ToArray(), token);
            Publications.Add(publication);
            PublishStarted?.TrySetResult();
            try
            {
                if (ReleasePublication is not null) await ReleasePublication.Task.WaitAsync(token);
            }
            finally { Interlocked.Decrement(ref activePublications); }
        }

        private async ValueTask CompleteDeliveryAsync(Dictionary<string, object?> arguments, bool acknowledge)
        {
            activeCompletions++;
            MaximumConcurrentCompletions = Math.Max(MaximumConcurrentCompletions, activeCompletions);
            Completions.Add(((ulong)arguments["deliveryTag"]!, acknowledge, (bool)arguments["multiple"]!,
                !acknowledge && (bool)arguments["requeue"]!));
            CompletionStarted?.TrySetResult();
            try
            {
                if (ReleaseCompletion is not null)
                    await ReleaseCompletion.Task.WaitAsync((CancellationToken)arguments["cancellationToken"]!);
            }
            finally { activeCompletions--; }
        }

        private static object FailedOperation(Type returnType, Exception failure)
        {
            if (returnType == typeof(ValueTask)) return ValueTask.FromException(failure);
            if (returnType == typeof(Task)) return Task.FromException(failure);
            if (returnType == typeof(Task<QueueDeclareOk>)) return Task.FromException<QueueDeclareOk>(failure);
            if (returnType == typeof(Task<string>)) return Task.FromException<string>(failure);
            throw new InvalidOperationException("Unsupported transport return type: " + returnType);
        }
    }
}
