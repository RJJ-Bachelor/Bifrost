using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;

namespace Shared.Api.Authentication;

public sealed class StudentCookieOptions : AuthenticationSchemeOptions
{
    public IDataProtectionProvider? DataProtectionProvider { get; set; }
}
