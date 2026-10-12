using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.Teacher.DeleteSession;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Sessions.DeleteSession;

internal sealed class DeleteSession : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/teachers/sessions/{sessionId:guid}");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(DeleteSession)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var command = new DeleteSessionCommand(Route<Guid>("sessionId"), User.GetRequiredUserId());
        await command.ExecuteAsync(cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }
}
