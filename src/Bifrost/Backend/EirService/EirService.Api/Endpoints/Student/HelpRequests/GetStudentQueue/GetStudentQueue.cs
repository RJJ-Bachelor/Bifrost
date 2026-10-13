using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Student.GetStudentQueue;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.HelpRequests.GetStudentQueue;

internal sealed class GetStudentQueue : EndpointWithoutRequest<StudentQueueResult>
{
    public override void Configure()
    {
        Get("/students/sessions/{sessionId:guid}/queue");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(GetStudentQueue)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new GetStudentQueueQuery(Route<Guid>("sessionId"), User.GetRequiredUserId()).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
