using EirService.HelpRequests.Application.Features.Commands.CreateHelpRequest;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.HelpRequests;

internal sealed class CreateHelpRequest : Endpoint<CreateHelpRequestRequest, string>
{
    public override void Configure()
    {
        Post("/students/createhelprequest");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(CreateHelpRequest)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CreateHelpRequestRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateHelpRequestCommand(
            request.Id, User.FindFirst("sub")?.Value ?? string.Empty, request.Message);
        var result = await command.ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
