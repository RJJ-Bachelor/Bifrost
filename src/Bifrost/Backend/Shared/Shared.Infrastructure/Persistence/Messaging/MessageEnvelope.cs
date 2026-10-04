namespace Shared.Infrastructure.Persistence.Messaging;

public sealed record MessageEnvelope<T>(string Id, T Payload);
