using FastEndpoints;
using GnaService.Api.Endpoints.Common.Notifications.PushSubscriptions;
using GnaService.Infrastructure.Push;
using Microsoft.Extensions.Options;

namespace GnaService.Api.Endpoints.Student.Notifications.GetPushPublicKey;

internal sealed class GetStudentPushPublicKey(IOptions<WebPushOptions> options) : EndpointWithoutRequest<PushPublicKeyResponse>
{
    public override void Configure()
    {
        Get("/students/notifications/push/public-key");
        Policies("Student");
        Options(builder => builder.WithName(nameof(GetStudentPushPublicKey)).WithTags("Notifications"));
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
