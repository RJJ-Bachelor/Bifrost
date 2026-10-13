using Microsoft.Extensions.Logging;
using MimirService.Application.Services.Messaging;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Test;

public sealed class SessionUpdatedMessageHandlerTests
{
    [Theory]
    [InlineData(SessionUpdateType.Cleared)]
    [InlineData(SessionUpdateType.Deleted)]
    [InlineData(SessionUpdateType.Archived)]
    public async Task KnownChangesLogSessionGenerationAndEventIdentity(SessionUpdateType eventType)
    {
        var logger = new RecordingLogger();
        var handler = new SessionUpdatedMessageHandler(logger);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            eventType, DateTimeOffset.UtcNow, 3);
        await handler.HandleAsync(new(update.EventId, update), CancellationToken.None);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains(eventType.ToString(), logged);
        Assert.Contains(update.SessionId.ToString(), logged);
        Assert.Contains(update.EventId.ToString(), logged);
        Assert.Contains("4", logged);
    }

    [Fact]
    public async Task UnknownChangeIsRejectedWithoutLoggingSuccess()
    {
        var logger = new RecordingLogger();
        var handler = new SessionUpdatedMessageHandler(logger);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            (SessionUpdateType)999, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.HandleAsync(new(update.EventId, update), CancellationToken.None));
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task CancelledDeliveryDoesNotRunTheHandler()
    {
        var logger = new RecordingLogger();
        var handler = new SessionUpdatedMessageHandler(logger);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            SessionUpdateType.Cleared, DateTimeOffset.UtcNow, 3);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(new(update.EventId, update), cancellation.Token));
        Assert.Empty(logger.Messages);
    }

    private sealed class RecordingLogger : ILogger<SessionUpdatedMessageHandler>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
