namespace MimirService.Application.Features.Development.GeneralizeHelpRequests;

public sealed record GeneralizeHelpRequestsResult(
    Guid GeneralizationId, Guid SessionId, int Generation, string Question, Guid[] HelpRequestIds);
