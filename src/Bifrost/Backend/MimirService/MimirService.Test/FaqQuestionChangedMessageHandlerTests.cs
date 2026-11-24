using Microsoft.Extensions.Logging.Abstractions;
using MimirService.Application.Repositories;
using MimirService.Application.Services.Messaging;
using MimirService.Domain.Entities;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace MimirService.Test;

public sealed class FaqQuestionChangedMessageHandlerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    [Fact]
    public async Task ChangedQuestionIsSavedOnTheGeneralization()
    {
        var repository = new FakeRepository();
        var generalization = repository.Add(SessionId, 1);

        await Handler(repository).HandleAsync(Message(generalization.Id, SessionId, 1, "  Revised question  "), CancellationToken.None);

        Assert.Equal("Revised question", generalization.Question);
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task UnknownFaqIsIgnored()
    {
        var repository = new FakeRepository();

        await Handler(repository).HandleAsync(Message(Guid.NewGuid(), SessionId, 1, "Revised question"), CancellationToken.None);

        Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task ChangeForAnotherSessionOrGenerationIsIgnored()
    {
        var repository = new FakeRepository();
        var generalization = repository.Add(SessionId, 1);

        await Handler(repository).HandleAsync(Message(generalization.Id, Guid.NewGuid(), 1, "Revised question"), CancellationToken.None);
        await Handler(repository).HandleAsync(Message(generalization.Id, SessionId, 2, "Revised question"), CancellationToken.None);

        Assert.Equal("Question", generalization.Question);
        Assert.Equal(0, repository.Saves);
    }

    [Theory]
    [InlineData(' ', 3)]
    [InlineData('a', Generalization.MaxQuestionLength + 1)]
    public async Task InvalidQuestionIsIgnoredSoTheMessageIsNotRetried(char character, int length)
    {
        var repository = new FakeRepository();
        var generalization = repository.Add(SessionId, 1);

        await Handler(repository).HandleAsync(Message(generalization.Id, SessionId, 1, new string(character, length)),
            CancellationToken.None);

        Assert.Equal("Question", generalization.Question);
        Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task CancelledDeliveryDoesNotRunTheHandler()
    {
        var repository = new FakeRepository();
        var generalization = repository.Add(SessionId, 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(repository)
            .HandleAsync(Message(generalization.Id, SessionId, 1, "Revised question"), cancellation.Token));

        Assert.Equal("Question", generalization.Question);
        Assert.Equal(0, repository.Saves);
    }

    private static FaqQuestionChangedMessageHandler Handler(FakeRepository repository) =>
        new(NullLogger<FaqQuestionChangedMessageHandler>.Instance, repository);

    private static MessageEnvelope<FaqQuestionChangedIntegrationEvent> Message(Guid faqId, Guid sessionId, int generation,
        string question) =>
        new(Guid.NewGuid(), new FaqQuestionChangedIntegrationEvent(faqId, sessionId, generation, "Question", question,
            "Answer", "teacher", DateTimeOffset.UtcNow));

    private sealed class FakeRepository : IHelpRequestRepository
    {
        private readonly List<Generalization> generalizations = [];

        public int Saves { get; private set; }

        public Generalization Add(Guid sessionId, int generation)
        {
            var generalization = Generalization.Create(Guid.NewGuid(), sessionId, generation, "Question", DateTimeOffset.UtcNow);
            generalizations.Add(generalization);
            return generalization;
        }

        public Task<Generalization?> GetGeneralizationAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(generalizations.SingleOrDefault(generalization => generalization.Id == id));

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
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
        public Task<(int Requests, int Generalizations)> DeleteSessionDataAsync(Guid sessionId, int? beforeGeneration,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
