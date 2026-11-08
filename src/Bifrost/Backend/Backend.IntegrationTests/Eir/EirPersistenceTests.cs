using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using EirService.Contracts.Exceptions;
using EirService.Contracts.HelpRequests.GetArchivedConversations;
using EirService.Contracts.HelpRequests.GetWaitingCounts;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.HelpRequests.Infrastructure.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.KnowledgeBase.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace Backend.IntegrationTests.Eir;

public sealed class EirPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task QueueQueriesPreserveFaqReturnAndFifoPriorityWithinTheRequestedGeneration()
    {
        await using var fixture = await RequestDatabase.CreateAsync();
        var sessionId = Guid.NewGuid();
        var first = Request(sessionId, "first", at: Now);
        var second = Request(sessionId, "second", at: Now.AddMinutes(1));
        var returned = Request(sessionId, "returned", at: Now.AddMinutes(2));
        returned.Reserve("teacher", Now.AddMinutes(3));
        returned.Cancel("teacher", AuthorRole.Teacher, Now.AddMinutes(4));
        var latestReturn = Request(sessionId, "latest-return", at: Now.AddMinutes(5));
        latestReturn.Reserve("teacher", Now.AddMinutes(6));
        latestReturn.Cancel("teacher", AuthorRole.Teacher, Now.AddMinutes(7));
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), sessionId, 1, new("Shared question"), Now.AddMinutes(8));
        await fixture.SeedAsync(first, second, returned, latestReturn, faq,
            Request(sessionId, "other-generation", 2), Request(Guid.NewGuid(), "other-session"));
        await using var db = fixture.Open();
        var repository = Repository(db);

        var queue = await repository.GetForSessionAsync(sessionId, 1, default);
        var activity = await repository.GetSessionActivityAsync(sessionId, 1, default);

        var expected = new[] { faq.Id, latestReturn.Id, returned.Id, first.Id, second.Id };
        Assert.Equal(expected, queue.Select(request => request.Id));
        Assert.Equal(expected, activity.Select(request => request.Id));
        Assert.Equal("Shared question", Assert.Single(queue[0].Messages).Text.Value);
        Assert.Equal("Question for first", Assert.Single(queue[3].Messages).Text.Value);
        Assert.Equal(latestReturn.ReturnedToQueueAt, queue[1].ReturnedToQueueAt);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task HistoryAndIdQueriesRoundTripMessagesAndDoNotLeakOtherStudents()
    {
        await using var fixture = await RequestDatabase.CreateAsync();
        var sessionId = Guid.NewGuid();
        var earlier = Request(sessionId, "student", at: Now);
        earlier.Reserve("teacher", Now.AddMinutes(1));
        earlier.Answer("teacher", new("Private answer"), Now.AddMinutes(2));
        var later = Request(sessionId, "student", 2, Now.AddMinutes(3));
        var other = Request(sessionId, "other");
        await fixture.SeedAsync(later, other, earlier);
        await using var db = fixture.Open();
        var repository = Repository(db);

        var history = await repository.GetByUserIdAsync("student", cancellationToken: default);

        Assert.Equal(new[] { earlier.Id, later.Id }, history.Select(request => request.Id));
        Assert.Equal(new[] { "Question for student", "Private answer" }, history[0].Messages.OrderBy(message => message.SequenceNumber).Select(message => message.Text.Value));
        Assert.Equal(new[] { 1, 2 }, history[0].Messages.OrderBy(message => message.SequenceNumber).Select(message => message.SequenceNumber));
        Assert.Empty(db.ChangeTracker.Entries());
        var byId = await repository.GetByIdsAsync([earlier.Id, other.Id, Guid.NewGuid()], default);
        Assert.Equal(new[] { earlier.Id, other.Id }.Order(), byId.Select(request => request.Id).Order());
        Assert.Equal(2, (await repository.GetAsync(earlier.Id, default))!.Messages.Count);
        Assert.Null(await repository.GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task WaitingCountsMatchExactSessionGenerationPairsAndExcludeReservedStudentsFromAvailability()
    {
        await using var fixture = await RequestDatabase.CreateAsync();
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        var waiting = Request(firstSession, "waiting");
        var reserved = Request(firstSession, "reserved");
        reserved.Reserve("teacher", Now);
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), firstSession, 1, new("Shared"), Now);
        var cancelled = Request(firstSession, "cancelled");
        cancelled.Cancel("cancelled", AuthorRole.Student, Now);
        await fixture.SeedAsync(waiting, reserved, faq, cancelled, Request(firstSession, "old", 2),
            Request(secondSession, "current", 2), Request(secondSession, "wrong-generation", 1));
        await using var db = fixture.Open();
        var repository = Repository(db);

        var counts = await repository.GetWaitingCountsAsync(new Dictionary<Guid, int> { [firstSession] = 1, [secondSession] = 2 }, default);

        Assert.Equal(new WaitingCounts(3, 2), counts[firstSession]);
        Assert.Equal(new WaitingCounts(1, 1), counts[secondSession]);
        Assert.Equal(new[] { "reserved", "waiting" }, (await repository.GetWaitingStudentIdsAsync(firstSession, 1, default)).Order());
        Assert.Empty(await repository.GetWaitingCountsAsync(new Dictionary<Guid, int>(), default));
        Assert.Empty(await repository.GetWaitingCountsAsync(new Dictionary<Guid, int> { [Guid.NewGuid()] = 1 }, default));
    }

    [Theory]
    [InlineData("pending", true)]
    [InlineData("reserved", true)]
    [InlineData("active", true)]
    [InlineData("awaiting-faq", true)]
    [InlineData("awaiting-confirmation", true)]
    [InlineData("completed", false)]
    [InlineData("cancelled", false)]
    public async Task OpenRequestQueryMatchesLifecycleAndAllScopeKeys(string state, bool isOpen)
    {
        await using var fixture = await RequestDatabase.CreateAsync();
        var request = Request(Guid.NewGuid(), "student", type: state == "active" ? HelpType.Physical : HelpType.Digital);
        if (state is "reserved" or "active" or "completed") request.Reserve("teacher", Now);
        if (state == "active") request.Start("teacher", Now);
        if (state == "completed") request.Answer("teacher", new("Answer"), Now);
        if (state == "cancelled") request.Cancel("student", AuthorRole.Student, Now);
        if (state is "awaiting-faq" or "awaiting-confirmation") request.LinkToFaq(Guid.NewGuid(), new("Shared"), Now);
        if (state == "awaiting-confirmation") request.SuggestFaq(request.FaqAnswerId!.Value, "teacher", new("Answer"), Now);
        await fixture.SeedAsync(request);
        await using var db = fixture.Open();
        var repository = Repository(db);

        Assert.Equal(isOpen, await repository.HasOpenAsync(request.SessionId, 1, "student", default));
        Assert.False(await repository.HasOpenAsync(Guid.NewGuid(), 1, "student", default));
        Assert.False(await repository.HasOpenAsync(request.SessionId, 2, "student", default));
        Assert.False(await repository.HasOpenAsync(request.SessionId, 1, "other-student", default));
    }

    [Fact]
    public async Task FaqLinkQueryReturnsOnlyLinkedStudentConversations()
    {
        await using var fixture = await RequestDatabase.CreateAsync();
        var sessionId = Guid.NewGuid();
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), sessionId, 1, new("Shared question"), Now);
        var linked = Request(sessionId, "linked");
        linked.LinkToFaq(faq.Id, new("Shared question"), Now);
        var different = Request(sessionId, "different");
        different.LinkToFaq(Guid.NewGuid(), new("Another question"), Now);
        await fixture.SeedAsync(faq, linked, different);
        await using var db = fixture.Open();

        var found = Assert.Single(await Repository(db).GetLinkedToFaqAsync(faq.Id, default));

        Assert.Equal(linked.Id, found.Id);
        Assert.Equal(new[] { HelpRequestMessageType.StudentQuestion, HelpRequestMessageType.FaqQuestion },
            found.Messages.OrderBy(message => message.SequenceNumber).Select(message => message.MessageType));
    }

    [Fact]
    public async Task RelationalConstraintsRejectDuplicateOpenRequestsWithoutPartialMessageWrites()
    {
        await using var fixture = await RequestDatabase.CreateAsync();
        var sessionId = Guid.NewGuid();
        await fixture.SeedAsync(Request(sessionId, "student"));
        await using var db = fixture.Open();
        var duplicate = Request(sessionId, "student");
        await Repository(db).AddAsync(duplicate, default);
        db.Entry(duplicate).Property(request => request.RequestNumber).CurrentValue = 2;

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var verification = fixture.Open();
        Assert.Single(await verification.Requests.ToArrayAsync());
        Assert.Single(await verification.Messages.ToArrayAsync());
    }

    [Fact]
    public async Task UnitOfWorkRollsBackDeletionAndOutboxTogetherAndCanRetry()
    {
        await using var fixture = await RequestDatabase.CreateAsync();
        var request = Request(Guid.NewGuid(), "student");
        await fixture.SeedAsync(request);
        await using var db = fixture.Open();
        var stored = (await Repository(db).GetAsync(request.Id, default))!;
        db.Requests.Remove(stored);
        db.Outbox.Add(OutboxMessage.Create(new OutgoingMessage(Guid.NewGuid(), new { Change = "Archived" }, "requests", "request.archived"), Now));
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_outbox BEFORE INSERT ON outbox BEGIN SELECT RAISE(ABORT, 'test rollback'); END;");
        var unitOfWork = new HelpRequestUnitOfWork(db, TimeProvider.System);

        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync());

        await using (var verification = fixture.Open())
        {
            Assert.Single(await verification.Requests.ToArrayAsync());
            Assert.Single(await verification.Messages.ToArrayAsync());
            Assert.Empty(await verification.Outbox.ToArrayAsync());
        }
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_outbox;");
        await unitOfWork.SaveChangesAsync();
        await using var committed = fixture.Open();
        Assert.Empty(await committed.Requests.ToArrayAsync());
        Assert.Empty(await committed.Messages.ToArrayAsync());
        Assert.Single(await committed.Outbox.ToArrayAsync());
    }

    [Fact]
    public async Task UnitOfWorkConvertsAConcurrentlyDeletedRowIntoAnOperationConflict()
    {
        await using var fixture = await RequestDatabase.CreateAsync();
        var request = Request(Guid.NewGuid(), "student");
        await fixture.SeedAsync(request);
        await using var stale = fixture.Open();
        var staleRequest = (await Repository(stale).GetAsync(request.Id, default))!;
        await using (var other = fixture.Open())
        {
            var removed = await other.Requests.SingleAsync();
            other.Requests.Remove(removed);
            await new HelpRequestUnitOfWork(other, TimeProvider.System).SaveChangesAsync();
        }
        stale.Requests.Remove(staleRequest);

        await Assert.ThrowsAsync<OperationConflictException>(() => new HelpRequestUnitOfWork(stale, TimeProvider.System).SaveChangesAsync());
    }

    [Fact]
    public async Task ArchiveReadsMergeLiveAndSnapshotConversationsWithPrivacyAndNoDuplicates()
    {
        await using var fixture = await RequestDatabase.CreateAsync();
        var sessionId = Guid.NewGuid();
        var completed = Request(sessionId, "student", at: Now);
        completed.Reserve("teacher", Now);
        completed.Answer("teacher", new("Private answer"), Now.AddMinutes(1));
        var cancelled = Request(sessionId, "other-student", at: Now.AddMinutes(2));
        cancelled.Cancel("other-student", AuthorRole.Student, Now.AddMinutes(3));
        var waiting = Request(sessionId, "waiting", at: Now.AddMinutes(4));
        var rejected = Request(sessionId, "rejected", at: Now.AddMinutes(5));
        var rejectedFaq = Guid.NewGuid();
        rejected.LinkToFaq(rejectedFaq, new("Rejected question"), Now);
        rejected.SuggestFaq(rejectedFaq, "teacher", new("Rejected answer"), Now);
        rejected.RejectFaq("rejected", Now);
        await fixture.SeedAsync(completed, cancelled, waiting, rejected);
        var snapshot = Snapshot(Guid.NewGuid(), sessionId, 1, "student", Now.AddMinutes(6), "Archived answer");
        await using (var seed = fixture.Open())
        {
            seed.ArchivedConversations.AddRange(snapshot,
                Snapshot(completed.Id, sessionId, 1, "student", Now, "Stale snapshot"),
                Snapshot(Guid.NewGuid(), sessionId, 2, "student", Now, "Other generation"),
                Snapshot(Guid.NewGuid(), Guid.NewGuid(), 1, "student", Now, "Other session"));
            await seed.SaveChangesAsync();
        }
        await using var db = fixture.Open();
        var repository = new ArchivedConversationRepository(db);

        var all = await repository.GetForGenerationAsync(sessionId, 1, null, default);
        var mine = await repository.GetForGenerationAsync(sessionId, 1, "student", default);

        Assert.Equal(new[] { completed.Id, cancelled.Id, snapshot.Id }, all.Select(conversation => conversation.Id));
        Assert.Equal(new[] { completed.Id, snapshot.Id }, mine.Select(conversation => conversation.Id));
        Assert.Equal(new[] { "Question for student", "Private answer" }, mine[0].Messages.Select(message => message.Text));
        Assert.Equal("Archived answer", Assert.Single(mine[1].Messages).Text);
        Assert.Empty(await repository.GetForGenerationAsync(sessionId, 1, "outsider", default));
        var counts = await repository.CountForGenerationsAsync(sessionId, [1, 2, 3], null, default);
        Assert.Equal(3, counts[1]);
        Assert.Equal(1, counts[2]);
        Assert.False(counts.ContainsKey(3));
        Assert.Equal(2, (await repository.CountForGenerationsAsync(sessionId, [1], "student", default))[1]);
        Assert.Empty(await repository.CountForGenerationsAsync(sessionId, [], null, default));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("{", true)]
    public async Task InvalidArchiveSnapshotsFailExplicitlyInsteadOfReturningAnEmptyConversation(string json, bool malformed)
    {
        await using var fixture = await RequestDatabase.CreateAsync();
        var sessionId = Guid.NewGuid();
        await using var db = fixture.Open();
        db.ArchivedConversations.Add(new() { Id = Guid.NewGuid(), SessionId = sessionId, Generation = 1, StudentId = "student", Data = json });
        await db.SaveChangesAsync();
        var repository = new ArchivedConversationRepository(db);

        if (malformed)
            await Assert.ThrowsAsync<JsonException>(() => repository.GetForGenerationAsync(sessionId, 1, "student", default));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetForGenerationAsync(sessionId, 1, "student", default));
    }

    [Fact]
    public async Task FaqRepositoryPreservesSourceIdsAndCountsAnswersBySessionAndGeneration()
    {
        await using var database = new SqliteTestDatabase();
        DbContextOptions<EirKnowledgeBaseDbContext> Options() =>
            new DbContextOptionsBuilder<EirKnowledgeBaseDbContext>(database.Options<EirKnowledgeBaseDbContext>())
                .ReplaceService<IModelCustomizer, EirPersistenceModelCustomizer>().Options;
        var sessionId = Guid.NewGuid();
        var pending = FaqAnswer.Create(Guid.NewGuid(), sessionId, 1, "Pending", [Guid.NewGuid(), Guid.NewGuid()], Now);
        var answered = FaqAnswer.CreateAnswered(sessionId, 1, "Answered", "Answer", "teacher", Now.AddMinutes(1));
        await using (var seed = new EirKnowledgeBaseDbContext(Options()))
        {
            await seed.Database.EnsureCreatedAsync();
            var repository = new FaqRepository(seed);
            foreach (var faq in new[] { pending, answered, FaqAnswer.CreateAnswered(sessionId, 2, "Other generation", "Answer", "teacher", Now),
                         FaqAnswer.CreateAnswered(Guid.NewGuid(), 1, "Other session", "Answer", "teacher", Now) })
            {
                faq.ClearDomainEvents();
                await repository.AddAsync(faq, default);
            }
            await seed.SaveChangesAsync();
        }
        await using var db = new EirKnowledgeBaseDbContext(Options());
        var faqs = new FaqRepository(db);

        var generation = await faqs.GetForSessionAsync(sessionId, 1, default);

        Assert.Equal(new[] { answered.Id, pending.Id }, generation.Select(faq => faq.Id));
        var persistedPending = Assert.Single(generation, faq => faq.Id == pending.Id);
        Assert.Equal(pending.HelpRequestIds, persistedPending.HelpRequestIds);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal("Answer", (await faqs.GetAsync(answered.Id, default))!.Answer);
        Assert.Null(await faqs.GetAsync(Guid.NewGuid(), default));
        var allCounts = await faqs.CountForGenerationsAsync(sessionId, [1, 2], true, default);
        var answerCounts = await faqs.CountForGenerationsAsync(sessionId, [1, 2], false, default);
        Assert.Equal(2, allCounts[1]);
        Assert.Equal(1, allCounts[2]);
        Assert.Equal(1, answerCounts[1]);
        Assert.Equal(1, answerCounts[2]);
        Assert.Empty(await faqs.CountForGenerationsAsync(sessionId, [], true, default));
    }

    private static HelpRequest Request(Guid sessionId, string studentId, int generation = 1,
        DateTimeOffset? at = null, HelpType type = HelpType.Digital) =>
        HelpRequest.Create(sessionId, generation, studentId, type, new("Question for " + studentId), at ?? Now);

    private static HelpRequestRepository Repository(EirRequestDbContext db) => new(db,
        new HelpRequestUnitOfWork(db, TimeProvider.System), TimeProvider.System);

    private static ArchivedConversationSnapshot Snapshot(Guid id, Guid sessionId, int generation, string student,
        DateTimeOffset createdAt, string text) => new()
    {
        Id = id, SessionId = sessionId, Generation = generation, StudentId = student,
        Data = JsonSerializer.Serialize(new ArchivedConversation(id, sessionId, generation, 99, "BF-099", student,
            "Digital", "Pending", null, createdAt, createdAt, null, null, null, null,
            [new(Guid.NewGuid(), id, "teacher", "Teacher", "TeacherAnswer", text, createdAt, 1)]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
    };

    private sealed class RequestDatabase : IAsyncDisposable
    {
        private readonly SqliteTestDatabase database = new();
        private readonly Dictionary<(Guid Session, int Generation), int> numbers = [];

        public EirRequestDbContext Open() => new(new DbContextOptionsBuilder<EirRequestDbContext>(database.Options<EirRequestDbContext>())
            .ReplaceService<IModelCustomizer, EirPersistenceModelCustomizer>().Options);

        public static async Task<RequestDatabase> CreateAsync()
        {
            var fixture = new RequestDatabase();
            try
            {
                await using var db = fixture.Open();
                await db.Database.EnsureCreatedAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task SeedAsync(params HelpRequest[] requests)
        {
            await using var db = Open();
            foreach (var request in requests)
            {
                request.ClearDomainEvents();
                await Repository(db).AddAsync(request, default);
                var key = (request.SessionId, request.Generation);
                var number = numbers.GetValueOrDefault(key) + 1;
                numbers[key] = number;
                // Fixture data supplies numbers; PostgreSQL's allocation trigger is tested separately.
                db.Entry(request).Property(value => value.RequestNumber).CurrentValue = number;
            }
            await db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => database.DisposeAsync();
    }
}

// Provider adapters only: these tests do not emulate PostgreSQL xmin, numbering triggers or advisory locks.
internal sealed class EirPersistenceModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        new SqliteDateTimeOffsetModelCustomizer(Dependencies).Customize(modelBuilder, context);
        if (context is EirRequestDbContext)
            modelBuilder.Entity<HelpRequest>().Property<uint>("xmin").HasDefaultValue(0u);
        if (context is EirKnowledgeBaseDbContext)
        {
            modelBuilder.Entity<FaqAnswer>().Property<uint>("xmin").HasDefaultValue(0u);
            var sources = modelBuilder.Entity<FaqAnswer>().Property<Guid[]>("helpRequestIds");
            sources.HasConversion(new ValueConverter<Guid[], string>(
                ids => JsonSerializer.Serialize(ids, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<Guid[]>(json, (JsonSerializerOptions?)null)!));
            sources.Metadata.SetValueComparer(new ValueComparer<Guid[]>(
                (first, second) => first!.SequenceEqual(second!),
                values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
                values => values.ToArray()));
        }
    }
}
