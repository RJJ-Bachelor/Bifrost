using EirService.Contracts.Sessions.Common;
using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.GetStudentSession;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.Sessions.GetStudentSession;

internal sealed class GetStudentSession : EndpointWithoutRequest<StudentSessionSummary>
{
    public override void Configure()
    {
        Get("/students/sessions/{sessionId:guid}");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(GetStudentSession)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var query = new GetStudentSessionQuery(Route<Guid>("sessionId"), User.GetRequiredUserId());
        var result = await query.ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
