using System.Security.Claims;

namespace EirService.Api.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static string GetRequiredUserId(this ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException(
            "The authenticated user has no subject claim.");
}
