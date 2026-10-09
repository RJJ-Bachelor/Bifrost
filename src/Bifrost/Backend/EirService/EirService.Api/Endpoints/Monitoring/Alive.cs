using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Monitoring;

internal sealed class Alive : EndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/monitoring/alive");
        AllowAnonymous();
        Options(builder => builder.WithName(nameof(Alive)).WithTags(EndpointTags.Monitoring));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        await Send.OkAsync("EirService is alive and running.", cancellationToken);
    }
}
