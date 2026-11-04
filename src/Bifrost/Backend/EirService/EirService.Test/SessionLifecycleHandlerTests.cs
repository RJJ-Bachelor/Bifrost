using EirService.Contracts.Exceptions;
using EirService.Sessions.Application.Features.Common.GetSessionArchive;
using EirService.Sessions.Application.Features.Common.GetSessionArchives;
using EirService.Sessions.Application.Features.Teacher.ArchiveSession;
using EirService.Sessions.Application.Features.Teacher.ClearSession;
using EirService.Sessions.Application.Features.Teacher.DeleteSession;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Domain.DomainEvents;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;
using Shared.Application.Persistence;

namespace EirService.Test;

public sealed class SessionLifecycleHandlerTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Changed = Started.AddHours(1);

    [Theory]
    [InlineData("clear")]
    [InlineData("archive")]
    [InlineData("delete")]
    public async Task MissingSessionDoesNotCommit(string operation)
    {
        var repository = new MemorySessionRepository();
        var commit = new RecordingCommit();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Execute(operation, repository, commit, Guid.NewGuid(), 1));

        Assert.Equal(0, commit.Calls);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("archive")]
    public async Task StaleGenerationDoesNotMutateOrCommit(string operation)
    {
        var (repository, session) = await CreateSession();
        var commit = new RecordingCommit();

        await Assert.ThrowsAsync<OperationConflictException>(() => Execute(operation, repository, commit, session.Id, 2));

        Assert.Equal(1, session.CurrentGeneration);
        Assert.Empty(session.Archives);
        Assert.Empty(session.GetDomainEvents());
        Assert.Equal(0, commit.Calls);
    }

    [Fact]
    public async Task ClearCommitsOneGenerationAndReturnsTheInjectedClock()
    {
        var (repository, session) = await CreateSession();
        var commit = new RecordingCommit();

        var result = await new ClearSessionCommandHandler(repository, commit, new FixedClock())
            .ExecuteAsync(new(session.Id, "owner", 1), default);

        Assert.Equal(new ClearSessionResult(session.Id, 2, Changed), result);
        Assert.Equal(2, session.CurrentGeneration);
        Assert.Equal(Changed, session.CurrentGenerationStartedAtUtc);
        Assert.IsType<SessionClearedDomainEvent>(Assert.Single(session.GetDomainEvents()));
        Assert.Equal(1, commit.Calls);
    }

    [Fact]
    public async Task ArchiveReturnsTheSnapshotAndStartsANewGeneration()
    {
        var (repository, session) = await CreateSession();
        var commit = new RecordingCommit();

        var result = await new ArchiveSessionCommandHandler(repository, commit, new FixedClock())
            .ExecuteAsync(new(session.Id, "owner", 1, "  Examination  "), default);

        var archive = Assert.Single(session.Archives);
        Assert.Equal((archive.Id, session.Id, "Examination", 1, 2, Started, Changed),
            (result.ArchiveId, result.SessionId, result.Title, result.Generation,
                result.CurrentGeneration, result.StartedAtUtc, result.ArchivedAtUtc));
        Assert.Equal("Course", session.Title);
        Assert.IsType<SessionArchivedDomainEvent>(Assert.Single(session.GetDomainEvents()));
        Assert.Equal(1, commit.Calls);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("archive")]
    [InlineData("delete")]
    public async Task OptimisticConcurrencyBecomesAnOperationConflict(string operation)
    {
        var (repository, session) = await CreateSession();
        var commit = new RecordingCommit { FailConcurrency = true };

        await Assert.ThrowsAsync<OperationConflictException>(() => Execute(operation, repository, commit, session.Id, 1));

        Assert.Equal(1, commit.Calls);
    }

    [Fact]
    public async Task OnlyTheOwnerCanDeleteASession()
    {
        var (repository, session) = await CreateSession();
        var commit = new RecordingCommit();
        var handler = new DeleteSessionCommandHandler(repository, commit, new FixedClock());

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => handler.ExecuteAsync(new(session.Id, "colleague"), default));
        Assert.Empty(session.GetDomainEvents());
        Assert.Equal(0, commit.Calls);

        await handler.ExecuteAsync(new(session.Id, "owner"), default);

        var deleted = Assert.IsType<SessionDeletedDomainEvent>(Assert.Single(session.GetDomainEvents()));
        Assert.Equal((session.Id, 1, Changed), (deleted.SessionId, deleted.Generation, deleted.DeletedAtUtc));
        Assert.Equal(1, commit.Calls);
        await repository.SaveChangesAsync();
        Assert.Null(await repository.GetAsync(session.Id, default));
    }

    [Fact]
    public async Task OwnerSeesAnEmptyArchiveListAndUnknownArchivesAreHidden()
    {
        var (repository, session) = await CreateSession();

        Assert.Empty(await new GetSessionArchivesQueryHandler(repository).ExecuteAsync(new(session.Id, "owner", true), default));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new GetSessionArchiveQueryHandler(repository)
            .ExecuteAsync(new(session.Id, Guid.NewGuid(), "owner", true), default));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new GetSessionArchivesQueryHandler(repository)
            .ExecuteAsync(new(Guid.NewGuid(), "owner", true), default));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(int.MaxValue - 1, true)]
    [InlineData(int.MaxValue, false)]
    public void ClearValidatorPreventsGenerationOverflow(int generation, bool valid)
    {
        var validator = new ClearSessionCommandValidator();
        var command = new ClearSessionCommand(Guid.NewGuid(), "owner", generation);

        Assert.Equal(valid, validator.Validate(command).IsValid);
        Assert.False(validator.Validate(command with { SessionId = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(command with { TeacherId = " " }).IsValid);
    }

    private static async Task<(MemorySessionRepository, Session)> CreateSession()
    {
        var repository = new MemorySessionRepository();
        var session = Session.Create("Course", new SessionCode("ABCD2345"), new SessionCode("EFGH6789"), "owner", null, Started);
        await repository.TryAddAsync(session, default);
        return (repository, session);
    }

    private static async Task Execute(string operation, ISessionRepository repository,
        IUnitOfWork<ISessionRepository> commit, Guid id, int generation)
    {
        switch (operation)
        {
            case "clear":
                await new ClearSessionCommandHandler(repository, commit, new FixedClock()).ExecuteAsync(new(id, "owner", generation), default);
                break;
            case "archive":
                await new ArchiveSessionCommandHandler(repository, commit, new FixedClock()).ExecuteAsync(new(id, "owner", generation), default);
                break;
            case "delete":
                await new DeleteSessionCommandHandler(repository, commit, new FixedClock()).ExecuteAsync(new(id, "owner"), default);
                break;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Changed;
    }

    private sealed class RecordingCommit : IUnitOfWork<ISessionRepository>
    {
        public int Calls { get; private set; }
        public bool FailConcurrency { get; init; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (FailConcurrency) throw new UnitOfWorkConcurrencyException(new InvalidOperationException("Simulated concurrent write"));
            return Task.CompletedTask;
        }
    }
}
