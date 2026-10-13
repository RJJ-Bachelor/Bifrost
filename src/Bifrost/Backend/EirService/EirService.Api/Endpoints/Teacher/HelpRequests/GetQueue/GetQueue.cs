using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Teacher.GetQueue;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.HelpRequests.GetQueue;

internal sealed class GetQueue : EndpointWithoutRequest<TeacherQueueResult>
{
    public override void Configure()
    {
        Get("/teachers/sessions/{sessionId:guid}/queue");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(GetQueue)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new GetQueueQuery(Route<Guid>("sessionId"), User.GetRequiredUserId()).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
