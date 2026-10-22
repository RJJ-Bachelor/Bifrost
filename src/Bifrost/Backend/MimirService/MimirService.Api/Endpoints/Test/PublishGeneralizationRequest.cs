namespace MimirService.Api.Endpoints.Test;

public sealed record PublishGeneralizationRequest(Guid SessionId, int Generation, string Question,
    Guid[] HelpRequestIds, Guid? GeneralizationId = null);
