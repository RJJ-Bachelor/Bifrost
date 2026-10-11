using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.GetHelpRequests;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.HelpRequests.GetHelpRequests;

internal sealed class GetHelpRequests : EndpointWithoutRequest<IReadOnlyList<HelpRequestResult>>
{
    public override void Configure()
    {
        Get("/students/requests");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(GetHelpRequests)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var query = new GetHelpRequestsQuery(User.GetRequiredUserId(), Query<Guid?>("sessionId", isRequired: false));
        var result = await query.ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
