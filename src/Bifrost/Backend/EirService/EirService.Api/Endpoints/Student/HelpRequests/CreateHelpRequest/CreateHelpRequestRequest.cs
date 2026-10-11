namespace EirService.Api.Endpoints.Student.HelpRequests.CreateHelpRequest;

public sealed class CreateHelpRequestRequest
{
    public Guid? SessionId { get; set; }
    public int? Generation { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
