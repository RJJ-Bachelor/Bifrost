using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.Test;

internal static class TestHelpRequests
{
    // Stable IDs are only used in broker redelivery fixtures.
    public static HelpRequest Create(Guid id, string studentId, string text, Guid? sessionId = null, int? generation = null)
    {
        var request = HelpRequest.Create(sessionId ?? Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            generation ?? 1, studentId, HelpType.Digital, new MessageText(text), DateTimeOffset.UtcNow);
        typeof(HelpRequest).GetProperty(nameof(HelpRequest.Id))!.SetValue(request, id);
        foreach (var message in request.Messages)
            typeof(HelpRequestMessage).GetProperty(nameof(HelpRequestMessage.HelpRequestId))!.SetValue(message, id);
        request.ClearDomainEvents();
        return request;
    }

    public static async Task SaveWithOutboxAsync(EirRequestDbContext db, HelpRequest request,
        IReadOnlyCollection<OutgoingMessage> outgoing, CancellationToken cancellationToken = default)
    {
        db.Requests.Add(request);
        db.Outbox.AddRange(outgoing.Select(message => OutboxMessage.Create(message, DateTimeOffset.UtcNow)));
        await new HelpRequestUnitOfWork(db, TimeProvider.System).SaveChangesAsync(cancellationToken);
    }
}
