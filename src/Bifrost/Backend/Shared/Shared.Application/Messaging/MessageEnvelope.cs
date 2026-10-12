namespace Shared.Application.Messaging;

public sealed record MessageEnvelope<T>(Guid Id, T Payload);
