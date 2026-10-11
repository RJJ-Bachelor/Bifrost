using EirService.Contracts.Sessions.Common;
using EirService.Api.Authentication;
using EirService.Participants.Application.Features.GetStudentSessions;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.Participants.GetStudentSessions;

internal sealed class GetStudentSessions : EndpointWithoutRequest<IReadOnlyList<StudentSessionSummary>>
{
    public override void Configure()
    {
        Get("/students/sessions");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(GetStudentSessions)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var query = new GetStudentSessionsQuery(User.GetRequiredUserId());
        var result = await query.ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
