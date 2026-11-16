using FastEndpoints;
using GnaService.Api.Authentication;
using GnaService.Api.Endpoints.Common.Notifications.PushSubscriptions;
using GnaService.Application.Features.Common.RegisterPushSubscription;

namespace GnaService.Api.Endpoints.Student.Notifications.RegisterPushSubscription;

internal sealed class RegisterStudentPushSubscription : Endpoint<RegisterPushSubscriptionRequest>
{
    public override void Configure()
    {
        Put("/students/notifications/push/subscriptions");
        Policies("Student");
        Options(builder => builder.WithName(nameof(RegisterStudentPushSubscription)).WithTags("Notifications"));
    }

    public override async Task HandleAsync(RegisterPushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        await new RegisterPushSubscriptionCommand(User.GetRequiredUserId(), request.Endpoint,
            request.Keys?.P256dh ?? string.Empty, request.Keys?.Auth ?? string.Empty).ExecuteAsync(cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }
}
