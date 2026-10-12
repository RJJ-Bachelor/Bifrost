using EirService.HelpRequests.Application.Features.Internal.SessionUpdated;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging.Contracts;

namespace EirService.Test;

public sealed class HelpRequestsSessionUpdatedMessageHandlerTests
{
    [Fact]
    public async Task DeletedLogsCleanupForAllSessionMessagesAcrossAllGenerations()
    {
        var logger = new RecordingLogger();
        var handler = new SessionUpdatedMessageHandler(logger);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            SessionUpdateType.Deleted, DateTimeOffset.UtcNow);
        await handler.HandleAsync(new(update.EventId, update), CancellationToken.None);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains(update.SessionId.ToString(), logged);
        Assert.Contains(update.EventId.ToString(), logged);
        Assert.Contains("all session messages across all generations", logged);
        Assert.Contains("not implemented yet", logged);
    }

    [Theory]
    [InlineData(SessionUpdateType.Cleared)]
    [InlineData(SessionUpdateType.Archived)]
    public async Task OtherSessionUpdatesDoNotRequestDeletion(SessionUpdateType eventType)
    {
        var logger = new RecordingLogger();
        var handler = new SessionUpdatedMessageHandler(logger);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4, eventType, DateTimeOffset.UtcNow);
        await handler.HandleAsync(new(update.EventId, update), CancellationToken.None);
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
