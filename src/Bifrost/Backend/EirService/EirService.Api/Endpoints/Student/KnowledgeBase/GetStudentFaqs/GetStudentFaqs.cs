using EirService.Api.Authentication;
using EirService.KnowledgeBase.Application.Features.Common;
using EirService.KnowledgeBase.Application.Features.Common.GetSessionFaqs;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.KnowledgeBase.GetStudentFaqs;

internal sealed class GetStudentFaqs : EndpointWithoutRequest<IReadOnlyList<FaqResult>>
{
    public override void Configure()
    {
        Get("/students/sessions/{sessionId:guid}/faqs");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(GetStudentFaqs)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new GetSessionFaqsQuery(Route<Guid>("sessionId"), User.GetRequiredUserId(), false)
            .ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
