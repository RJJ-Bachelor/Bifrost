using EirService.Api.Authentication;
using EirService.KnowledgeBase.Application.Features.Common;
using EirService.KnowledgeBase.Application.Features.GetSessionFaqs;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.KnowledgeBase.GetTeacherFaqs;

internal sealed class GetTeacherFaqs : EndpointWithoutRequest<IReadOnlyList<FaqResult>>
{
    public override void Configure()
    {
        Get("/teachers/sessions/{sessionId:guid}/faqs");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(GetTeacherFaqs)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new GetSessionFaqsQuery(Route<Guid>("sessionId"), User.GetRequiredUserId(), true)
            .ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
