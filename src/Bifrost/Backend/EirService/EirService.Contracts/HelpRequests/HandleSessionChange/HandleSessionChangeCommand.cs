using FastEndpoints;

namespace EirService.Contracts.HelpRequests.HandleSessionChange;

public sealed record HandleSessionChangeCommand(
    Guid EventId,
    Guid SessionId,
    int Generation,
    SessionChangeType EventType,
    DateTimeOffset UpdatedAtUtc,
    int? PreviousGeneration = null) : ICommand;
