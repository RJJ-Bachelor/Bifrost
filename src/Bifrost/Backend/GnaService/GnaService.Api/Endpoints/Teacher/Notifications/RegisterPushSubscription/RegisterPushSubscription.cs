using FastEndpoints;
using GnaService.Api.Authentication;
using GnaService.Api.Endpoints.Common.Notifications.PushSubscriptions;
using GnaService.Application.Features.Common.RegisterPushSubscription;

namespace GnaService.Api.Endpoints.Teacher.Notifications.RegisterPushSubscription;

internal sealed class RegisterTeacherPushSubscription : Endpoint<RegisterPushSubscriptionRequest>
{
    public override void Configure()
    {
        Put("/teachers/notifications/push/subscriptions");
        Policies("Teacher");
        Options(builder => builder.WithName(nameof(RegisterTeacherPushSubscription)).WithTags("Notifications"));
    }

    public override async Task HandleAsync(RegisterPushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        await new RegisterPushSubscriptionCommand(User.GetRequiredUserId(), request.Endpoint,
            request.Keys?.P256dh ?? string.Empty, request.Keys?.Auth ?? string.Empty).ExecuteAsync(cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }
}
