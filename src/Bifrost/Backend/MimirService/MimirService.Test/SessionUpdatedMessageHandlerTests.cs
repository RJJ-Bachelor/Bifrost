using Microsoft.Extensions.Logging;
using MimirService.Application.Repositories;
using MimirService.Application.Services.Messaging;
using MimirService.Domain.Entities;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

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
        var handler = new SessionUpdatedMessageHandler(logger, new RecordingRepository());
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
    public async Task DeletedSessionDeletesItsDataInEveryGeneration()
    {
        var repository = new RecordingRepository();
        var handler = new SessionUpdatedMessageHandler(new RecordingLogger(), repository);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            SessionUpdateType.Deleted, DateTimeOffset.UtcNow);
        await handler.HandleAsync(new(update.EventId, update), CancellationToken.None);
        Assert.Equal((update.SessionId, (int?)null), Assert.Single(repository.Deletes));
    }

    [Fact]
    public async Task ArchivedSessionKeepsItsData()
    {
        var repository = new RecordingRepository();
        var handler = new SessionUpdatedMessageHandler(new RecordingLogger(), repository);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            SessionUpdateType.Archived, DateTimeOffset.UtcNow, 3);
        await handler.HandleAsync(new(update.EventId, update), CancellationToken.None);
        Assert.Empty(repository.Deletes);
    }

    [Fact]
    public async Task UnknownChangeIsRejectedWithoutLoggingSuccess()
    {
        var logger = new RecordingLogger();
        var repository = new RecordingRepository();
        var handler = new SessionUpdatedMessageHandler(logger, repository);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            (SessionUpdateType)999, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.HandleAsync(new(update.EventId, update), CancellationToken.None));
        Assert.Empty(logger.Messages);
        Assert.Empty(repository.Deletes);
    }

    [Fact]
    public async Task CancelledDeliveryDoesNotRunTheHandler()
    {
        var logger = new RecordingLogger();
        var repository = new RecordingRepository();
        var handler = new SessionUpdatedMessageHandler(logger, repository);
        var update = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 4,
            SessionUpdateType.Deleted, DateTimeOffset.UtcNow);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(new(update.EventId, update), cancellation.Token));
        Assert.Empty(logger.Messages);
        Assert.Empty(repository.Deletes);
    }

    private sealed class RecordingLogger : ILogger<SessionUpdatedMessageHandler>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class RecordingRepository : IHelpRequestRepository
    {
        public List<(Guid SessionId, int? BeforeGeneration)> Deletes { get; } = [];

        public Task<(int Requests, int Generalizations)> DeleteSessionDataAsync(Guid sessionId, int? beforeGeneration,
            CancellationToken cancellationToken)
        {
            Deletes.Add((sessionId, beforeGeneration));
            return Task.FromResult((2, 1));
        }

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<HelpRequest>> GetWaitingAsync(Guid sessionId, int generation, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Generalization>> GetGeneralizationsAsync(Guid sessionId, int generation,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetGeneralizationMemberIdsAsync(Guid generalizationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryAddAsync(HelpRequest request, Generalization? generalization, OutgoingMessage? reply,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
