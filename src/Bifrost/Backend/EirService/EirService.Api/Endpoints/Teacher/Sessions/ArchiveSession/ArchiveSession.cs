using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.Teacher.ArchiveSession;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Sessions.ArchiveSession;

internal sealed class ArchiveSession : Endpoint<ArchiveSessionRequest, ArchiveSessionResult>
{
    public override void Configure()
    {
        Post("/teachers/sessions/{sessionId:guid}/archive");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(ArchiveSession)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(ArchiveSessionRequest request, CancellationToken cancellationToken)
    {
        var command = new ArchiveSessionCommand(Route<Guid>("sessionId"), User.GetRequiredUserId(), request.ExpectedGeneration, request.Title);
        await Send.OkAsync(await command.ExecuteAsync(cancellationToken), cancellationToken);
    }
}
