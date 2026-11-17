namespace Shared.Application.Messaging;

public sealed record MessageMetadata(
    string Type, int Version, DateTimeOffset OccurredAtUtc,
    Guid? CorrelationId = null, Guid? CausationId = null);
