using Shared.Domain.DomainEvents;

namespace EirService.Sessions.Domain.DomainEvents;

public sealed record SessionDeletedDomainEvent(Guid SessionId, int Generation, DateTimeOffset DeletedAtUtc)
    : DomainEvent(Guid.NewGuid());
