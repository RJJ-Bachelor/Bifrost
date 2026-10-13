using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Teacher.TakeNextHelpRequest;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.HelpRequests.TakeNextHelpRequest;

internal sealed class TakeNextHelpRequest : EndpointWithoutRequest<HelpRequestResult>
{
    public override void Configure()
    {
        Post("/teachers/sessions/{sessionId:guid}/queue/take-next");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(TakeNextHelpRequest)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new TakeNextHelpRequestCommand(Route<Guid>("sessionId"), User.GetRequiredUserId()).ExecuteAsync(cancellationToken);
        if (result is null) await Send.NoContentAsync(cancellationToken);
        else await Send.OkAsync(result, cancellationToken);
    }
}
