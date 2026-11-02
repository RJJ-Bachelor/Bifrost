using EirService.KnowledgeBase.Application.Features.Internal.FaqAnswered;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;

namespace EirService.Test;

public sealed class FaqAnsweredMessageHandlerUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingFaqDoesNotCommitAnOrphanAnswer()
    {
        var repository = new RecordingFaqRepository();
        var message = new FaqAnsweredMessage(Guid.NewGuid(), Guid.NewGuid(), 1, "Answer", "teacher", Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new FaqAnsweredMessageHandler(repository, repository).HandleAsync(message, default));

        Assert.Equal(0, repository.Commits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncorrectSessionOrGenerationDoesNotChangeTheFaq(bool wrongSession)
    {
        var faq = CreateFaq();
        var repository = new RecordingFaqRepository(faq);
        var message = new FaqAnsweredMessage(faq.Id, wrongSession ? Guid.NewGuid() : faq.SessionId,
            wrongSession ? faq.Generation : faq.Generation + 1, "Answer", "teacher", Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new FaqAnsweredMessageHandler(repository, repository).HandleAsync(message, default));

        Assert.Null(faq.Answer);
        Assert.Empty(faq.GetDomainEvents());
        Assert.Equal(0, repository.Commits);
    }

    [Fact]
    public async Task IdenticalRedeliveryPreservesTheFirstAnswerAndItsTimestamp()
    {
        var faq = CreateFaq();
        var repository = new RecordingFaqRepository(faq);
        var handler = new FaqAnsweredMessageHandler(repository, repository);
        var message = new FaqAnsweredMessage(faq.Id, faq.SessionId, faq.Generation, " Answer ", "teacher", Now, " Revised question ");

        await handler.HandleAsync(message, default);
        await handler.HandleAsync(message with { AnsweredAt = Now.AddMinutes(1) }, default);

        Assert.Equal("Revised question", faq.Question);
        Assert.Equal("Answer", faq.Answer);
        Assert.Equal(Now, faq.AnsweredAt);
        Assert.Equal("teacher", faq.AnsweredByTeacherId);
        Assert.Single(faq.GetDomainEvents());
        Assert.Equal(2, repository.Commits);
    }

    [Fact]
    public async Task ContradictoryRedeliveryDoesNotOverwriteTheFirstAnswer()
    {
        var faq = CreateFaq();
        faq.RecordAnswer("First answer", "teacher", Now);
        var repository = new RecordingFaqRepository(faq);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new FaqAnsweredMessageHandler(repository, repository)
            .HandleAsync(new(faq.Id, faq.SessionId, faq.Generation, "Conflicting answer", "teacher", Now.AddMinutes(1)), default));

        Assert.Equal("First answer", faq.Answer);
        Assert.Equal(Now, faq.AnsweredAt);
        Assert.Equal(0, repository.Commits);
    }

    private static FaqAnswer CreateFaq() => FaqAnswer.Create(Guid.NewGuid(), Guid.NewGuid(), 1,
        "Question", [Guid.NewGuid(), Guid.NewGuid()], Now);

    private sealed class RecordingFaqRepository(FaqAnswer? faq = null) : IFaqRepository, IUnitOfWork<IFaqRepository>
    {
        public int Commits { get; private set; }
        public Task<FaqAnswer?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(faq?.Id == id ? faq : null);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commits++;
            return Task.CompletedTask;
        }
        public Task AddAsync(FaqAnswer value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, int>> CountForGenerationsAsync(Guid sessionId, int[] generations, bool includeUnanswered,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<FaqAnswer>> GetForSessionAsync(Guid sessionId, int generation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
