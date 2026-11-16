using FastEndpoints;

namespace GnaService.Application.Features.Common.UnregisterPushSubscription;

public sealed record UnregisterPushSubscriptionCommand(string UserId, string Endpoint) : ICommand;
