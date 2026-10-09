namespace Shared.Application.Messaging;

public sealed record MessageEnvelope<T>(string Id, T Payload);
