using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Student.CancelHelpRequest;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.HelpRequests.CancelHelpRequest;

internal sealed class CancelHelpRequest : Endpoint<CancelHelpRequestRequest, HelpRequestResult>
{
    public override void Configure()
    {
        Patch("/students/requests/{id:guid}/status");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(CancelHelpRequest)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancelHelpRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await new CancelHelpRequestCommand(Route<Guid>("id"), User.GetRequiredUserId(), request.Action).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
