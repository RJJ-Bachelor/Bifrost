using System.Security.Claims;

namespace GnaService.Api.Authentication;

internal static class ClaimsPrincipalExtensions
{
    internal static string GetRequiredUserId(this ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value ?? throw new InvalidOperationException("The authenticated user has no subject claim.");
}
