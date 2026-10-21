using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Student.RespondToFaq;
using FastEndpoints;
using FluentValidation;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.HelpRequests.RespondToFaq;

internal sealed class RespondToFaq : Endpoint<RespondToFaqRequest, HelpRequestResult>
{
    public override void Configure()
    {
        Patch("/students/requests/{id:guid}/faq");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(RespondToFaq)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(RespondToFaqRequest request, CancellationToken cancellationToken)
    {
        var result = await new RespondToFaqCommand(Route<Guid>("id"), User.GetRequiredUserId(), request.Accept!.Value).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}

public sealed record RespondToFaqRequest(bool? Accept);

internal sealed class RespondToFaqValidator : Validator<RespondToFaqRequest>
{
    public RespondToFaqValidator() => RuleFor(request => request.Accept).NotNull();
}
