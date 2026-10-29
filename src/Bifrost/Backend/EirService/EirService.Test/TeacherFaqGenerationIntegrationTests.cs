using System.Net;
using EirService.Contracts.Exceptions;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shared.Application.Persistence;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task TeacherFaqCannotCommitToAGenerationClearedWhileItWasBeingCreated()
    {
        var session = await CreateAsync();
        await using var pending = app.Services.CreateAsyncScope();
        var repository = pending.ServiceProvider.GetRequiredService<IFaqRepository>();
        var unitOfWork = pending.ServiceProvider.GetRequiredService<IUnitOfWork<IFaqRepository>>();
        var faq = FaqAnswer.CreateAnswered(session.Id, 1, "Teacher question", "Answer",
            "provider|opaque-teacher", clock.Now);
        await repository.AddAsync(faq, CancellationToken.None);

        using var clear = await ClearAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);

        await Assert.ThrowsAsync<OperationConflictException>(() => unitOfWork.SaveChangesAsync());

        await using var verification = app.Services.CreateAsyncScope();
        var database = verification.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>();
        Assert.Empty(await database.Faqs.ToArrayAsync());
        Assert.Empty(await database.Outbox.ToArrayAsync());
    }

    [PostgresFact]
    public async Task SessionDeletionHoldsTheFaqGenerationLockUntilCommitAndRejectsPendingTeacherFaq()
    {
        var created = await CreateAsync();
        await using var pending = app.Services.CreateAsyncScope();
        var faqRepository = pending.ServiceProvider.GetRequiredService<IFaqRepository>();
        var faqUnitOfWork = pending.ServiceProvider.GetRequiredService<IUnitOfWork<IFaqRepository>>();
        var faq = FaqAnswer.CreateAnswered(created.Id, created.CurrentGeneration, "Teacher question", "Answer",
            "provider|opaque-teacher", clock.Now);
        await faqRepository.AddAsync(faq, CancellationToken.None);

        await using var deleting = app.Services.CreateAsyncScope();
        var sessionRepository = deleting.ServiceProvider.GetRequiredService<ISessionRepository>();
        var sessionDatabase = deleting.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var session = (await sessionRepository.GetAsync(created.Id, CancellationToken.None))!;
        await using var transaction = await sessionDatabase.Database.BeginTransactionAsync();
        session.Delete(clock.Now);
        sessionRepository.Remove(session);
        await deleting.ServiceProvider.GetRequiredService<IUnitOfWork<ISessionRepository>>().SaveChangesAsync();

        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync();
        await using var tryLock = new NpgsqlCommand(
            "SELECT pg_try_advisory_xact_lock(hashtextextended(@key, 0))", observer);
        tryLock.Parameters.AddWithValue("key", $"helprequests:{created.Id}:{created.CurrentGeneration}");
        // A separate FAQ writer cannot acquire its generation lock before the deletion commits.
        Assert.False((bool)(await tryLock.ExecuteScalarAsync())!);

        await transaction.CommitAsync();
        Assert.True((bool)(await tryLock.ExecuteScalarAsync())!);
        await Assert.ThrowsAsync<OperationConflictException>(() => faqUnitOfWork.SaveChangesAsync());

        await using var verification = app.Services.CreateAsyncScope();
        Assert.Empty(await verification.ServiceProvider.GetRequiredService<EirSessionDbContext>().Sessions.ToArrayAsync());
        var database = verification.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>();
        Assert.Empty(await database.Faqs.ToArrayAsync());
        Assert.Empty(await database.Outbox.ToArrayAsync());
    }
}
