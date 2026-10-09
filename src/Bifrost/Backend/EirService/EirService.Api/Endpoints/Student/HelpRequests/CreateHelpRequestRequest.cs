namespace EirService.Api.Endpoints.Student.HelpRequests;

public sealed class CreateHelpRequestRequest
{
    public string Id { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
