using FastEndpoints;
using GnaService.Api.Authentication;
using GnaService.Api.Endpoints.Common.Notifications.PushSubscriptions;
using GnaService.Application.Features.Common.UnregisterPushSubscription;

namespace GnaService.Api.Endpoints.Teacher.Notifications.UnregisterPushSubscription;

internal sealed class UnregisterTeacherPushSubscription : Endpoint<UnregisterPushSubscriptionRequest>
{
    public override void Configure()
    {
        Delete("/teachers/notifications/push/subscriptions");
        Policies("Teacher");
        Options(builder => builder.WithName(nameof(UnregisterTeacherPushSubscription)).WithTags("Notifications"));
    }

    public override async Task HandleAsync(UnregisterPushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        await new UnregisterPushSubscriptionCommand(User.GetRequiredUserId(), request.Endpoint).ExecuteAsync(cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }
}
