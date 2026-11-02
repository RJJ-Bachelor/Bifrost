using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Shared.Api.Authentication;

namespace Backend.IntegrationTests.Infrastructure;

internal sealed class TestAuthentication
{
    private readonly SymmetricSecurityKey key = new(RandomNumberGenerator.GetBytes(32));

    public void Register(WebApplicationBuilder builder)
    {
        var protection = new EphemeralDataProtectionProvider();
        // Run before the production configure callback so it never creates a disk key ring.
        builder.Services.Configure<StudentCookieOptions>(StudentCookieHandler.SchemeName,
            options => options.DataProtectionProvider = protection);
        builder.Services.AddBifrostServiceAuthentication(builder.Configuration, builder.Environment);
        builder.Services.RemoveAll<IDataProtectionProvider>();
        builder.Services.AddSingleton<IDataProtectionProvider>(protection);
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "backend-integration-tests",
                ValidateAudience = true, ValidAudience = "Bifrost",
                ValidateIssuerSigningKey = true, IssuerSigningKey = key,
                ValidateLifetime = true, NameClaimType = "name", RoleClaimType = "role",
                ClockSkew = TimeSpan.Zero
            };
        });
    }

    public string Token(string subject, string role = "Teacher", string scope = "Bifrost",
        string audience = "Bifrost", DateTime? expires = null) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "backend-integration-tests", audience: audience,
            claims: [new Claim("sub", subject), new Claim("name", "Integration Teacher"),
                new Claim("role", role), new Claim("scope", scope)],
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

    public void Teacher(HttpClient client, string subject, string role = "Teacher", string scope = "Bifrost") =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(subject, role, scope));

    public static void Student(HttpClient client, IServiceProvider services, string subject) =>
        client.DefaultRequestHeaders.Add("Cookie", StudentCookieHandler.CookieName + "=" +
            services.GetRequiredService<StudentIdentityCookie>().Protect(subject));
}
