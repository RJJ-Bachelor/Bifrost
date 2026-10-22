using FastEndpoints;

namespace EirService.Contracts.HelpRequests.CanApplyGeneralization;

public sealed record CanApplyGeneralizationQuery(
    Guid GeneralizationId,
    Guid SessionId,
    int Generation,
    Guid[] HelpRequestIds) : ICommand<bool>;
