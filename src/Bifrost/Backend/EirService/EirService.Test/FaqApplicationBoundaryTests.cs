using EirService.Contracts.Exceptions;
using EirService.KnowledgeBase.Application.Features.Common.GetSessionFaqs;
using EirService.KnowledgeBase.Application.Features.Teacher.CreateFaq;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using FastEndpoints;
using Shared.Application.Persistence;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    [Fact]
    public async Task JoinedTeacherCanPublishAFaqForTheCurrentGeneration()
    {
        var session = await new CreateSessionCommand("owner", "Course").ExecuteAsync();
        await new JoinTeacherSessionCommand(session.TeacherCode, "colleague").ExecuteAsync();
        var repository = new MemoryApplicationFaqRepository();
        var clock = new FaqClock();

        var result = await new CreateFaqCommandHandler(repository, repository, clock)
            .ExecuteAsync(new(session.Id, "colleague", "  Question  ", "  Answer  "), default);

        var stored = Assert.Single(repository.Faqs);
        Assert.Equal((session.Id, 1, "Question", "Answer", "colleague", clock.GetUtcNow()),
            (stored.SessionId, stored.Generation, stored.Question, stored.Answer, stored.AnsweredByTeacherId, stored.AnsweredAt));
        Assert.Equal(stored.Id, result.Id);
        Assert.Single(stored.GetDomainEvents());
        Assert.Equal(1, repository.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishingAFaqRequiresAnExistingSessionAndTeacherMembership(bool missingSession)
    {
        var session = await new CreateSessionCommand("owner", "Course").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "student").ExecuteAsync();
        var repository = new MemoryApplicationFaqRepository();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new CreateFaqCommandHandler(repository, repository, new FaqClock())
            .ExecuteAsync(new(missingSession ? Guid.NewGuid() : session.Id, "student", "Question", "Answer"), default));

        Assert.Empty(repository.Faqs);
        Assert.Equal(0, repository.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StudentsSeeAnsweredFaqsAndTeachersAlsoSeePendingFaqs(bool isTeacher)
    {
        var session = await new CreateSessionCommand("owner", "Course").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "student").ExecuteAsync();
        var repository = new MemoryApplicationFaqRepository();
        var answered = FaqAnswer.CreateAnswered(session.Id, 1, "Answered", "Answer", "owner", DateTimeOffset.UtcNow);
        repository.Faqs.Add(answered);
        repository.Faqs.Add(FaqAnswer.Create(Guid.NewGuid(), session.Id, 1, "Pending", [Guid.NewGuid(), Guid.NewGuid()], DateTimeOffset.UtcNow));
        repository.Faqs.Add(FaqAnswer.CreateAnswered(session.Id, 2, "Other generation", "Answer", "owner", DateTimeOffset.UtcNow));

        var result = await new GetSessionFaqsQueryHandler(repository)
            .ExecuteAsync(new(session.Id, isTeacher ? "owner" : "student", isTeacher), default);

        Assert.Equal(isTeacher ? 2 : 1, result.Count);
        Assert.Contains(result, faq => faq.Id == answered.Id && faq.Answer == "Answer");
        Assert.DoesNotContain(result, faq => faq.Generation != 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonMembersCannotReadSessionFaqs(bool isTeacher)
    {
        var session = await new CreateSessionCommand("owner", "Course").ExecuteAsync();
        var repository = new MemoryApplicationFaqRepository();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new GetSessionFaqsQueryHandler(repository)
            .ExecuteAsync(new(session.Id, "outsider", isTeacher), default));

        Assert.Equal(0, repository.Reads);
    }

    private sealed class FaqClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class MemoryApplicationFaqRepository : IFaqRepository, IUnitOfWork<IFaqRepository>
    {
        public List<FaqAnswer> Faqs { get; } = [];
        public int Commits { get; private set; }
        public int Reads { get; private set; }
        public Task<FaqAnswer?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Faqs.SingleOrDefault(faq => faq.Id == id));
        public Task AddAsync(FaqAnswer faq, CancellationToken cancellationToken)
        {
            Faqs.Add(faq);
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commits++;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<FaqAnswer>> GetForSessionAsync(Guid sessionId, int generation, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<FaqAnswer>>(Faqs.Where(faq => faq.SessionId == sessionId && faq.Generation == generation).ToArray());
        }
        public Task<IReadOnlyDictionary<int, int>> CountForGenerationsAsync(Guid sessionId, int[] generations, bool includeUnanswered,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
