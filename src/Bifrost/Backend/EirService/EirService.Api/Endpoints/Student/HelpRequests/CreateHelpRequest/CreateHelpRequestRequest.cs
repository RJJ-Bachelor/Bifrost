using EirService.HelpRequests.Domain.Entities;

namespace EirService.Api.Endpoints.Student.HelpRequests.CreateHelpRequest;

public sealed class CreateHelpRequestRequest
{
    public string Message { get; set; } = string.Empty;
    public HelpType? HelpType { get; set; }
}
