using Shared.Domain.DomainEvents;

namespace EirService.Sessions.Domain.DomainEvents;

public sealed record SessionClearedDomainEvent(
    Guid SessionId, int PreviousGeneration, int CurrentGeneration, DateTimeOffset ClearedAtUtc)
    : DomainEvent(Guid.NewGuid());
