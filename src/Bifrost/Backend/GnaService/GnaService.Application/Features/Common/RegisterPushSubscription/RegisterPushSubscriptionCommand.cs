using FastEndpoints;

namespace GnaService.Application.Features.Common.RegisterPushSubscription;

public sealed record RegisterPushSubscriptionCommand(string UserId, string Endpoint, string P256dh, string Auth) : ICommand;
