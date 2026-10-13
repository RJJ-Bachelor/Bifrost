using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Teacher.AnswerHelpRequest;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.HelpRequests.AnswerHelpRequest;

internal sealed class AnswerHelpRequest : Endpoint<AnswerHelpRequestRequest, HelpRequestResult>
{
    public override void Configure()
    {
        Post("/teachers/requests/{id:guid}/messages");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(AnswerHelpRequest)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(AnswerHelpRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await new AnswerHelpRequestCommand(Route<Guid>("id"), User.GetRequiredUserId(), request.Text).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
