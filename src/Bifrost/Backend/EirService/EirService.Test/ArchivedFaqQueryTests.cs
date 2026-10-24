using EirService.Contracts.KnowledgeBase.GetArchivedFaqs;
using EirService.KnowledgeBase.Application.Features.Internal.GetArchivedFaqCounts;
using EirService.KnowledgeBase.Application.Features.Internal.GetArchivedFaqs;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EirService.Test;

public sealed class ArchivedFaqQueryTests
{
    [Fact]
    public async Task ArchiveReadsRequestedGenerationAndPreservesUnansweredQuestionsForTeachers()
    {
        var sessionId = Guid.NewGuid();
        var answered = FaqAnswer.Create(Guid.NewGuid(), sessionId, 1, "Answered question",
            [Guid.NewGuid(), Guid.NewGuid()], DateTimeOffset.UtcNow);
        answered.RecordAnswer("Answer", "teacher", DateTimeOffset.UtcNow);
        var unanswered = FaqAnswer.Create(Guid.NewGuid(), sessionId, 1, "Unanswered question",
            [Guid.NewGuid(), Guid.NewGuid()], DateTimeOffset.UtcNow);
        var later = FaqAnswer.Create(Guid.NewGuid(), sessionId, 2, "New generation question",
            [Guid.NewGuid(), Guid.NewGuid()], DateTimeOffset.UtcNow);
        var repository = new MemoryFaqs([answered, unanswered, later]);
        var handler = new GetArchivedFaqsQueryHandler(repository);
        var teacher = await handler.ExecuteAsync(new(sessionId, 1, true), CancellationToken.None);
        Assert.Equal(new[] { answered.Id, unanswered.Id }, teacher.Select(faq => faq.Id));
        Assert.Null(teacher[1].Answer);
        var student = await handler.ExecuteAsync(new(sessionId, 1, false), CancellationToken.None);
        Assert.Equal(answered.Id, Assert.Single(student).Id);
        Assert.Equal("Answer", student[0].Answer);
        var countHandler = new GetArchivedFaqCountsQueryHandler(repository);
        var teacherCounts = await countHandler.ExecuteAsync(new(sessionId, [1, 2, 3], true), CancellationToken.None);
        Assert.Equal(2, teacherCounts[1]);
        Assert.Equal(1, teacherCounts[2]);
        Assert.False(teacherCounts.ContainsKey(3));
        var studentCounts = await countHandler.ExecuteAsync(new(sessionId, [1, 2, 3], false), CancellationToken.None);
        Assert.Equal(1, Assert.Single(studentCounts).Value);
        Assert.True(studentCounts.ContainsKey(1));
    }

    [Fact]
    public void ArchivedConversationMigrationMatchesTheCurrentModel()
    {
        using var db = new EirRequestDbContext(new DbContextOptionsBuilder<EirRequestDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var script = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
            .GenerateScript("20261022220000_HideRejectedFaqMessages", "20261023090000_ArchiveQueuedConversations");
        Assert.Contains("CREATE TABLE helprequests.\"ArchivedConversation\"", script);
        Assert.Contains("jsonb", script);
    }

    private sealed class MemoryFaqs(IReadOnlyList<FaqAnswer> faqs) : IFaqRepository
    {
        public Task<FaqAnswer?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(faqs.SingleOrDefault(faq => faq.Id == id));
        public Task AddAsync(FaqAnswer faq, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, int>> CountForGenerationsAsync(
            Guid sessionId, int[] generations, bool includeUnanswered, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<int, int>>(faqs
                .Where(faq => faq.SessionId == sessionId && generations.Contains(faq.Generation) &&
                    (includeUnanswered || faq.Answer is not null))
                .GroupBy(faq => faq.Generation).ToDictionary(group => group.Key, group => group.Count()));
        public Task<IReadOnlyList<FaqAnswer>> GetForSessionAsync(Guid sessionId, int generation, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FaqAnswer>>(faqs.Where(faq => faq.SessionId == sessionId && faq.Generation == generation).ToArray());
    }
}

