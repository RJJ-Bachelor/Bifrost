using FastEndpoints;

namespace EirService.Contracts.HelpRequests.ApplyGeneralization;

public sealed record ApplyGeneralizationCommand(
    Guid GeneralizationId,
    Guid SessionId,
    int Generation,
    string Question,
    Guid[] HelpRequestIds) : ICommand<bool>;
