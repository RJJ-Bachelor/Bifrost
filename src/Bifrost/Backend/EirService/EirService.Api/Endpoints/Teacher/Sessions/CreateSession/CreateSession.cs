using EirService.Sessions.Application.Features.Common;
using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.CreateSession;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Sessions.CreateSession;

internal sealed class CreateSession : Endpoint<CreateSessionRequest, SessionResult>
{
    public override void Configure()
    {
        Post("/teachers/sessions");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(CreateSession)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CreateSessionRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateSessionCommand(User.GetRequiredUserId(), request.Title, User.FindFirst("name")?.Value);
        var result = await command.ExecuteAsync(cancellationToken);
        HttpContext.Response.Headers.Location = $"/api/teachers/sessions/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, cancellationToken);
    }
}
