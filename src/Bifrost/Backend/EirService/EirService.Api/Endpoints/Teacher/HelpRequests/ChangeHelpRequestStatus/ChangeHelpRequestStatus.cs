using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Teacher.ChangeHelpRequestStatus;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.HelpRequests.ChangeHelpRequestStatus;

internal sealed class ChangeHelpRequestStatus : Endpoint<ChangeHelpRequestStatusRequest, HelpRequestResult>
{
    public override void Configure()
    {
        Patch("/teachers/requests/{id:guid}/status");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(ChangeHelpRequestStatus)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(ChangeHelpRequestStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await new ChangeHelpRequestStatusCommand(Route<Guid>("id"), User.GetRequiredUserId(), request.Action).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
