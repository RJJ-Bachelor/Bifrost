namespace EirService.Api.Endpoints.Student.HelpRequests.CreateHelpRequest;

public sealed class CreateHelpRequestRequest
{
    public Guid? SessionId { get; set; }
    public int? Generation { get; set; }
    public Guid Id { get; set; }
    public string Message { get; set; } = string.Empty;
}
