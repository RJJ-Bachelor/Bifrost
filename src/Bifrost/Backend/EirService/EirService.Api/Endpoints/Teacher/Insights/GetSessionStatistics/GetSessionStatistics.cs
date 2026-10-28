using EirService.Api.Authentication;
using EirService.Insights.Application.Features.Teacher.GetSessionStatistics;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Insights.GetSessionStatistics;

internal sealed class GetSessionStatistics : EndpointWithoutRequest<SessionStatisticsResult>
{
    public override void Configure()
    {
        Get("/teachers/sessions/{sessionId:guid}/statistics");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(GetSessionStatistics)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new GetSessionStatisticsQuery(Route<Guid>("sessionId"), User.GetRequiredUserId()).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
