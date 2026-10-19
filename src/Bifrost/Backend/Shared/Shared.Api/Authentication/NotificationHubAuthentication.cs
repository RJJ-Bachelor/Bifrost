using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Shared.Api.Authentication;

public static class NotificationHubAuthentication
{
    public const string StudentPath = "/api/students/notifications";
    public const string TeacherPath = "/api/teachers/notifications";

    public static bool HasQueryToken(HttpRequest request) =>
        request.Path.StartsWithSegments(TeacherPath) && !string.IsNullOrEmpty(request.Query["access_token"]);

    public static Task ReadQueryToken(MessageReceivedContext context)
    {
        // Browser WebSockets cannot supply an Authorization header. Accept this only on the teacher hub.
        if (HasQueryToken(context.Request) &&
            !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            context.Token = context.Request.Query["access_token"];
        return Task.CompletedTask;
    }
}
