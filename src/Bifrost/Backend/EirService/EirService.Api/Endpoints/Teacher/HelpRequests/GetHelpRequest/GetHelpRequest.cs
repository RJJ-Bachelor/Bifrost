using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Teacher.GetHelpRequest;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.HelpRequests.GetHelpRequest;

internal sealed class GetHelpRequest : EndpointWithoutRequest<HelpRequestResult>
{
    public override void Configure()
    {
        Get("/teachers/requests/{id:guid}");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(GetHelpRequest)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new GetHelpRequestQuery(Route<Guid>("id"), User.GetRequiredUserId()).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
