using EirService.Contracts.HelpRequests.GetArchivedConversations;
using EirService.HelpRequests.Application.Features.Internal.SessionUpdated;
using EirService.HelpRequests.Application.Repositories;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging.Contracts;

namespace EirService.Test;

public sealed class HelpRequestsSessionUpdatedMessageHandlerTests
{
    [Fact]
    public async Task DeletedLogsCleanupForAllSessionMessagesAcrossAllGenerations()
    {
        var logger = new RecordingLogger();
        var repository = new RecordingArchives();
        var handler = new SessionUpdatedMessageHandler(repository, logger);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            SessionUpdateType.Deleted, DateTimeOffset.UtcNow);
        await handler.HandleAsync(new(update.EventId, update), CancellationToken.None);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains(update.SessionId.ToString(), logged);
        Assert.Contains(update.EventId.ToString(), logged);
        Assert.Contains("all session messages across all generations", logged);
        Assert.Contains("not implemented yet", logged);
        Assert.Empty(repository.Cleanups);
    }

    [Fact]
    public async Task ClearedDoesNotRunArchiveCleanup()
    {
        var logger = new RecordingLogger();
        var repository = new RecordingArchives();
        var handler = new SessionUpdatedMessageHandler(repository, logger);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            SessionUpdateType.Cleared, DateTimeOffset.UtcNow, 3);
        await handler.HandleAsync(new(update.EventId, update), CancellationToken.None);
        Assert.Empty(logger.Messages);
        Assert.Empty(repository.Cleanups);
    }

    [Fact]
    public async Task ArchivedCleansOnlyThePreviousGeneration()
    {
        var repository = new RecordingArchives();
        var handler = new SessionUpdatedMessageHandler(repository, new RecordingLogger());
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            SessionUpdateType.Archived, DateTimeOffset.UtcNow, 3);
        await handler.HandleAsync(new(update.EventId, update), CancellationToken.None);
        Assert.Equal((update.SessionId, 3), Assert.Single(repository.Cleanups));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task InvalidArchiveCannotDeleteCurrentOrFutureGeneration(int? previousGeneration)
    {
        var repository = new RecordingArchives();
        var handler = new SessionUpdatedMessageHandler(repository, new RecordingLogger());
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            SessionUpdateType.Archived, DateTimeOffset.UtcNow, previousGeneration);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(new(update.EventId, update), CancellationToken.None));
        Assert.Empty(repository.Cleanups);
    }

    private sealed class RecordingArchives : IArchivedConversationRepository
    {
        public List<(Guid, int)> Cleanups { get; } = [];
        public Task ArchiveQueueAsync(Guid sessionId, int generation, CancellationToken cancellationToken)
        {
            Cleanups.Add((sessionId, generation));
            return Task.CompletedTask;
        }
        public Task<IReadOnlyDictionary<int, int>> CountForGenerationsAsync(
            Guid sessionId, int[] generations, string? studentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ArchivedConversation>> GetForGenerationAsync(
            Guid sessionId, int generation, string? studentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
