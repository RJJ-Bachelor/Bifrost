using System.Data.Common;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.HelpRequests.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shared.Infrastructure.Persistence.UnitOfWork;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task QueueStartProbeExcludesCurrentRequestOtherSessionsAndGenerationsWithoutLoadingMessages()
    {
        var session = await CreateAsync("Queue-start probe");
        var foreign = await CreateAsync("Other session");
        var current = QueueCountRequest(session.Id, 2);
        var completed = QueueCountRequest(session.Id, 2);
        completed.Reserve(session.CreatedByTeacherId, clock.Now);
        completed.Answer(session.CreatedByTeacherId, new MessageText("Completed answer"), clock.Now);
        var active = QueueCountRequest(session.Id, 2, HelpType.Physical);
        active.Reserve(session.CreatedByTeacherId, clock.Now);
        active.Start(session.CreatedByTeacherId, clock.Now);
        var cancelled = QueueCountRequest(session.Id, 2);
        cancelled.Cancel(cancelled.StudentId!, AuthorRole.Student, clock.Now);
        var awaitingFaq = QueueCountRequest(session.Id, 2);
        awaitingFaq.LinkToFaq(Guid.NewGuid(), new MessageText("Shared question"), clock.Now);
        var awaitingConfirmation = QueueCountRequest(session.Id, 2, HelpType.Physical);
        var faqId = Guid.NewGuid();
        awaitingConfirmation.LinkToFaq(faqId, new MessageText("Shared question"), clock.Now);
        awaitingConfirmation.SuggestFaq(faqId, session.CreatedByTeacherId, new MessageText("Suggested answer"), clock.Now);
        await SeedQueueCountRequestsAsync(current, completed, active, cancelled, awaitingFaq, awaitingConfirmation,
            QueueCountRequest(session.Id, 1), QueueCountRequest(session.Id, 3), QueueCountRequest(foreign.Id, 2));

        var capture = new QueueStartQueryCapture();
        await using var db = new EirRequestDbContext(new DbContextOptionsBuilder<EirRequestDbContext>()
            .UseNpgsql(connectionString).AddInterceptors(capture).Options);
        var repository = new HelpRequestRepository(db, new UnitOfWork<IHelpRequestRepository, EirRequestDbContext>(db, clock), clock);

        Assert.False(await repository.HasOtherPendingAsync(session.Id, 2, current.Id, CancellationToken.None));
        var sql = Assert.Single(capture.Commands);
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HelpRequestMessage", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.ChangeTracker.Entries());
        // The excluded entity is the only pending request in the selected session/generation.
        Assert.True(await repository.HasOtherPendingAsync(session.Id, 2, Guid.NewGuid(), CancellationToken.None));
    }

    [PostgresFact]
    public async Task QueueStartProbeCountsReservedPendingRequestsAndFaqs()
    {
        var session = await CreateAsync("Reserved pending request");
        var faqSession = await CreateAsync("Pending FAQ");
        var current = QueueCountRequest(session.Id, 1);
        var reserved = QueueCountRequest(session.Id, 1, HelpType.Physical);
        reserved.Reserve(session.CreatedByTeacherId, clock.Now);
        Assert.Equal(HelpRequestStatus.Pending, reserved.Status);
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), faqSession.Id, 1, new MessageText("Shared question"), clock.Now);
        await SeedQueueCountRequestsAsync(current, reserved, faq);

        await using var db = new EirRequestDbContext(new DbContextOptionsBuilder<EirRequestDbContext>()
            .UseNpgsql(connectionString).Options);
        var repository = new HelpRequestRepository(db, new UnitOfWork<IHelpRequestRepository, EirRequestDbContext>(db, clock), clock);
        Assert.True(await repository.HasOtherPendingAsync(session.Id, 1, current.Id, CancellationToken.None));
        Assert.True(await repository.HasOtherPendingAsync(faqSession.Id, 1, Guid.NewGuid(), CancellationToken.None));
        Assert.False(await repository.HasOtherPendingAsync(faqSession.Id, 1, faq.Id, CancellationToken.None));
    }

    private sealed class QueueStartQueryCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
