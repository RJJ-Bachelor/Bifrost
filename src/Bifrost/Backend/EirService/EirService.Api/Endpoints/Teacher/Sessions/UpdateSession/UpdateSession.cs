using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Features.Teacher.UpdateSession;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Sessions.UpdateSession;

internal sealed class UpdateSession : Endpoint<UpdateSessionRequest, SessionResult>
{
    public override void Configure()
    {
        Patch("/teachers/sessions/{sessionId:guid}");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(UpdateSession)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(UpdateSessionRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateSessionCommand(Route<Guid>("sessionId"), User.GetRequiredUserId(), request.Title);
        var result = await command.ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
