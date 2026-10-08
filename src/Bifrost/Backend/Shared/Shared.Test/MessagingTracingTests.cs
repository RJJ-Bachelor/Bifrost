using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Infrastructure.Persistence.Messaging;

namespace Shared.Test;

public sealed class MessagingTracingTests : IDisposable
{
    private readonly List<Activity> _completed = [];
    private readonly ActivityListener _listener;

    public MessagingTracingTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessageBusTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _completed.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    [Fact]
    public async Task HttpRequestAndTwoMessageDeliveriesShareOneTrace()
    {
        var channel = CreateChannel();
        BasicProperties firstProperties = new();
        BasicProperties replyProperties = new();
        ActivityTraceId traceId;
        ActivitySpanId firstPublisherId;
        ActivitySpanId replyPublisherId = default;

        using (var request = new Activity("HTTP request").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            request.AddBaggage("workflow", "request-created");
            request.TraceStateString = "vendor=state";
            traceId = request.TraceId;
            using var publish = MessageBusTelemetry.StartPublishActivity("requests", "request.created", "first");
            Assert.NotNull(publish);
            Assert.Equal(request.SpanId, publish.ParentSpanId);
            firstPublisherId = publish.SpanId;
            MessageBusTelemetry.InjectContext(firstProperties);
        }

        // Model an unrelated activity inherited by the background subscription.
        using var subscription = new Activity("subscription").SetIdFormat(ActivityIdFormat.W3C).Start();
        await RabbitMqMessageBus.HandleMessageAsync<string>(channel, "mimir-request-created",
            Delivery(firstProperties), async (message, _) =>
            {
                await Task.Yield();
                var process = Assert.IsType<Activity>(Activity.Current);
                Assert.Equal(traceId, process.TraceId);
                Assert.Equal(firstPublisherId, process.ParentSpanId);
                Assert.True(process.HasRemoteParent);
                Assert.Equal("vendor=state", process.TraceStateString);
                Assert.Equal("request-created", process.GetBaggageItem("workflow"));
                Assert.Equal("first", message.Id);

                using var reply = MessageBusTelemetry.StartPublishActivity("requests", "generalized.created", "reply");
                Assert.NotNull(reply);
                Assert.Equal(process.SpanId, reply.ParentSpanId);
                replyPublisherId = reply.SpanId;
                MessageBusTelemetry.InjectContext(replyProperties);
            }, CancellationToken.None);

        Assert.Same(subscription, Activity.Current);
        await RabbitMqMessageBus.HandleMessageAsync<string>(channel, "eir-generalized-created",
            Delivery(replyProperties, routingKey: "generalized.created"), (_, _) =>
            {
                var process = Assert.IsType<Activity>(Activity.Current);
                Assert.Equal(traceId, process.TraceId);
                Assert.Equal(replyPublisherId, process.ParentSpanId);
                return Task.CompletedTask;
            }, CancellationToken.None);

        Assert.Same(subscription, Activity.Current);
        Assert.Equal(2, ((AcknowledgementChannel)(object)channel).Acknowledgements);
        Assert.Equal(4, _completed.Count);
        Assert.All(_completed, activity => Assert.Equal(traceId, activity.TraceId));
        Assert.All(((AcknowledgementChannel)(object)channel).AckActivities,
            activity => Assert.Equal(ActivityKind.Consumer, activity?.Kind));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-traceparent")]
    public async Task LegacyOrInvalidHeadersStartANewTrace(string? traceParent)
    {
        using var subscription = new Activity("subscription").SetIdFormat(ActivityIdFormat.W3C).Start();
        var properties = new BasicProperties
        {
            Headers = traceParent is null ? null : new Dictionary<string, object?> { ["traceparent"] = traceParent }
        };

        await RabbitMqMessageBus.HandleMessageAsync<string>(CreateChannel(), "queue", Delivery(properties), (_, _) =>
        {
            var process = Assert.IsType<Activity>(Activity.Current);
            Assert.NotEqual(subscription.TraceId, process.TraceId);
            Assert.Equal(default, process.ParentSpanId);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Same(subscription, Activity.Current);
    }

    [Fact]
    public async Task StringHeadersFromOtherPublishersAreAccepted()
    {
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?> { ["traceparent"] = traceParent }
        };

        await RabbitMqMessageBus.HandleMessageAsync<string>(CreateChannel(), "queue", Delivery(properties), (_, _) =>
        {
            Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", Activity.Current?.TraceId.ToString());
            Assert.Equal("00f067aa0ba902b7", Activity.Current?.ParentSpanId.ToString());
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HandlerFailureIsTracedWithoutAcknowledgingTheMessage()
    {
        var channel = CreateChannel();
        var error = new InvalidOperationException("Handler failed");
        using var subscription = new Activity("subscription").Start();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RabbitMqMessageBus.HandleMessageAsync<string>(channel, "queue", Delivery(new BasicProperties()),
                (_, _) => Task.FromException(error), CancellationToken.None));

        Assert.Same(error, thrown);
        Assert.Same(subscription, Activity.Current);
        Assert.Equal(0, ((AcknowledgementChannel)(object)channel).Acknowledgements);
        var process = Assert.Single(_completed);
        Assert.Equal(ActivityStatusCode.Error, process.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, process.GetTagItem("error.type"));
        Assert.Contains(process.Events, activityEvent => activityEvent.Name == "exception");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    public async Task InvalidPayloadIsTracedWithoutCallingTheHandler(string body)
    {
        var channel = CreateChannel();
        var handlerCalled = false;
        var delivery = Delivery(new BasicProperties(), body: Encoding.UTF8.GetBytes(body));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            RabbitMqMessageBus.HandleMessageAsync<string>(channel, "queue", delivery, (_, _) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            }, CancellationToken.None));

        Assert.False(handlerCalled);
        Assert.Equal(0, ((AcknowledgementChannel)(object)channel).Acknowledgements);
        Assert.Equal(ActivityStatusCode.Error, Assert.Single(_completed).Status);
    }

    [Fact]
    public void ServiceDefaultsSubscribeToMessagingAndProxySpans()
    {
        _listener.Dispose();
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        using var host = builder.Build();
        _ = host.Services.GetService(typeof(TracerProvider));
        using var proxySource = new ActivitySource("Yarp.ReverseProxy");
        using var proxy = proxySource.StartActivity("proxy");
        using var publish = MessageBusTelemetry.StartPublishActivity("requests", "request.created", "first");

        Assert.NotNull(proxy);
        Assert.NotNull(publish);
        Assert.Equal(proxy.TraceId, publish.TraceId);
        Assert.Equal(proxy.SpanId, publish.ParentSpanId);
    }

    private static BasicDeliverEventArgs Delivery(BasicProperties properties,
        string routingKey = "request.created", byte[]? body = null)
    {
        return new BasicDeliverEventArgs("consumer", 42, false, "requests", routingKey, properties,
            body ?? JsonSerializer.SerializeToUtf8Bytes(new { id = "first", payload = "message" }));
    }

    private static IChannel CreateChannel() => DispatchProxy.Create<IChannel, AcknowledgementChannel>();

    public void Dispose() => _listener.Dispose();

    public class AcknowledgementChannel : DispatchProxy
    {
        public int Acknowledgements { get; private set; }
        public List<Activity?> AckActivities { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(IChannel.BasicAckAsync), targetMethod?.Name);
            Assert.Equal(42UL, args?[0]);
            Assert.Equal(false, args?[1]);
            Acknowledgements++;
            AckActivities.Add(Activity.Current);
            return ValueTask.CompletedTask;
        }
    }
}
