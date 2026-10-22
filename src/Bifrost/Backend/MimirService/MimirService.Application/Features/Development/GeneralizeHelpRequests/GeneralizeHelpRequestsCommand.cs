namespace MimirService.Application.Features.Development.GeneralizeHelpRequests;

public sealed record GeneralizeHelpRequestsCommand(
    Guid SessionId, int Generation, Guid[]? HelpRequestIds, Guid? GeneralizationId = null);
