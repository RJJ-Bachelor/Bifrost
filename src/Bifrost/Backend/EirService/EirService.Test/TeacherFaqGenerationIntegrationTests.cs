using System.Net;
using EirService.Contracts.Exceptions;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
}
