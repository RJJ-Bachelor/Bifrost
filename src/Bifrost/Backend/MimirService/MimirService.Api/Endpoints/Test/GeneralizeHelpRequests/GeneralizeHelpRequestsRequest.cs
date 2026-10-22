namespace MimirService.Api.Endpoints.Test.GeneralizeHelpRequests;

public sealed class GeneralizeHelpRequestsRequest
{
    public Guid SessionId { get; set; }
    public int Generation { get; set; }
    public Guid[]? HelpRequestIds { get; set; }
    public Guid? GeneralizationId { get; set; }
}
