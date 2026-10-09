using EirService.Sessions.Application.Features.CreateSession;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Session;

internal sealed class CreateSession : EndpointWithoutRequest<bool>
{
    public override void Configure()
    {
        Post("/teachers/createsession");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(CreateSession)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var command = new CreateSessionCommand(User.FindFirst("sub")?.Value ?? string.Empty);
        var result = await command.ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
