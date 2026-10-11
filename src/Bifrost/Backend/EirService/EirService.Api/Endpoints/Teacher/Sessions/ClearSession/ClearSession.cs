using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.ClearSession;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Sessions.ClearSession;

internal sealed class ClearSession : Endpoint<ClearSessionRequest, ClearSessionResult>
{
    public override void Configure()
    {
        Post("/teachers/sessions/{sessionId:guid}/clear");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(ClearSession)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(ClearSessionRequest request, CancellationToken cancellationToken)
    {
        var command = new ClearSessionCommand(
            Route<Guid>("sessionId"), User.GetRequiredUserId(), request.ExpectedGeneration);
        var result = await command.ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
