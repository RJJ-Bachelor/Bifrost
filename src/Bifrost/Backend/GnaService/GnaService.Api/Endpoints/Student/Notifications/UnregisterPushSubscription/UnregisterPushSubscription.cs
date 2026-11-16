using FastEndpoints;
using GnaService.Api.Authentication;
using GnaService.Api.Endpoints.Common.Notifications.PushSubscriptions;
using GnaService.Application.Features.Common.UnregisterPushSubscription;

namespace GnaService.Api.Endpoints.Student.Notifications.UnregisterPushSubscription;

internal sealed class UnregisterStudentPushSubscription : Endpoint<UnregisterPushSubscriptionRequest>
{
    public override void Configure()
    {
        Delete("/students/notifications/push/subscriptions");
        Policies("Student");
        Options(builder => builder.WithName(nameof(UnregisterStudentPushSubscription)).WithTags("Notifications"));
    }

    public override async Task HandleAsync(UnregisterPushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        await new UnregisterPushSubscriptionCommand(User.GetRequiredUserId(), request.Endpoint).ExecuteAsync(cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }
}
