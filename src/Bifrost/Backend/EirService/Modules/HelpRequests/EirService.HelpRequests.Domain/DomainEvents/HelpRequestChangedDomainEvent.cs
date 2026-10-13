using EirService.HelpRequests.Domain.Entities;
using Shared.Domain.DomainEvents;

namespace EirService.HelpRequests.Domain.DomainEvents;

public sealed record HelpRequestChangedDomainEvent(HelpRequest Request, string Change, DateTimeOffset OccurredAt)
    : DomainEvent(Guid.NewGuid());
