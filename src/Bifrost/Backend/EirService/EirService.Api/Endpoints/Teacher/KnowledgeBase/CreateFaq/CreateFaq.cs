using EirService.Api.Authentication;
using EirService.KnowledgeBase.Application.Features.Common;
using EirService.KnowledgeBase.Application.Features.Teacher.CreateFaq;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.KnowledgeBase.CreateFaq;

internal sealed class CreateFaq : Endpoint<CreateFaqRequest, FaqResult>
{
    public override void Configure()
    {
        Post("/teachers/sessions/{sessionId:guid}/faqs");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(CreateFaq)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CreateFaqRequest request, CancellationToken cancellationToken)
    {
        var result = await new CreateFaqCommand(Route<Guid>("sessionId"), User.GetRequiredUserId(),
            request.Question, request.Answer).ExecuteAsync(cancellationToken);
        await Send.ResponseAsync(result, StatusCodes.Status201Created, cancellationToken);
    }
}
