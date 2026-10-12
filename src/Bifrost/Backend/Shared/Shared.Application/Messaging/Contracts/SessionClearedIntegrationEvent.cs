namespace Shared.Application.Messaging.Contracts;

public sealed record SessionClearedIntegrationEvent(
    Guid EventId, Guid SessionId, int PreviousGeneration, int CurrentGeneration, DateTimeOffset ClearedAtUtc);
