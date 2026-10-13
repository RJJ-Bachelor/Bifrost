using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.HelpRequests.CreateHelpRequest;

internal sealed class CreateHelpRequest : Endpoint<CreateHelpRequestRequest, HelpRequestResult>
{
    public override void Configure()
    {
        Post("/students/sessions/{sessionId:guid}/requests");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(CreateHelpRequest)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CreateHelpRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await new CreateHelpRequestCommand(
            Route<Guid>("sessionId"), User.GetRequiredUserId(), request.Message, request.HelpType).ExecuteAsync(cancellationToken);
        HttpContext.Response.Headers.Location = $"/api/students/requests/{result.Id}";
        await Send.ResponseAsync(result, 201, cancellationToken);
    }
}
