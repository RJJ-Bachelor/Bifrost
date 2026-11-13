using FastEndpoints;
using GnaService.Api.Endpoints.Common.Notifications.PushSubscriptions;
using GnaService.Infrastructure.Push;
using Microsoft.Extensions.Options;

namespace GnaService.Api.Endpoints.Teacher.Notifications.GetPushPublicKey;

internal sealed class GetTeacherPushPublicKey(IOptions<WebPushOptions> options) : EndpointWithoutRequest<PushPublicKeyResponse>
{
    public override void Configure()
    {
        Get("/teachers/notifications/push/public-key");
        Policies("Teacher");
        Options(builder => builder.WithName(nameof(GetTeacherPushPublicKey)).WithTags("Notifications"));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            await Send.StatusCodeAsync(StatusCodes.Status503ServiceUnavailable, cancellationToken);
            return;
        }
        await Send.OkAsync(new PushPublicKeyResponse(options.Value.PublicKey), cancellationToken);
    }
}
