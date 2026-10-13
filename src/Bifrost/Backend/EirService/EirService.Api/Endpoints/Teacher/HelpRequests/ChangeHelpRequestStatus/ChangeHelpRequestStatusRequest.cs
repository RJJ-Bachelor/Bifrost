using EirService.HelpRequests.Application.Features.Teacher.ChangeHelpRequestStatus;

namespace EirService.Api.Endpoints.Teacher.HelpRequests.ChangeHelpRequestStatus;

public sealed class ChangeHelpRequestStatusRequest
{
    public HelpRequestAction? Action { get; set; }
}
