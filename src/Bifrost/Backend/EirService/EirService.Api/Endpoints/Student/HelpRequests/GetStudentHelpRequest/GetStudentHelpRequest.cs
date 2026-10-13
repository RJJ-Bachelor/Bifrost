using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Student.GetStudentHelpRequest;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.HelpRequests.GetStudentHelpRequest;

internal sealed class GetStudentHelpRequest : EndpointWithoutRequest<HelpRequestResult>
{
    public override void Configure()
    {
        Get("/students/requests/{id:guid}");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(GetStudentHelpRequest)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new GetStudentHelpRequestQuery(Route<Guid>("id"), User.GetRequiredUserId()).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
