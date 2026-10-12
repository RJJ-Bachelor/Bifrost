using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Shared.Api.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static AuthenticationBuilder AddStudentCookie(this AuthenticationBuilder builder,
        IConfiguration configuration, Action<StudentCookieOptions>? configure = null)
    {
        builder.Services.TryAddSingleton<StudentIdentityCookie>();
        return builder.AddScheme<StudentCookieOptions, StudentCookieHandler>(StudentCookieHandler.SchemeName, options =>
        {
            configure?.Invoke(options);
            if (options.DataProtectionProvider is not null) return;

            var keyRingPath = configuration["Authentication:StudentCookie:KeyRingPath"];
            if (string.IsNullOrWhiteSpace(keyRingPath))
                keyRingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Bifrost", "StudentIdentityKeys");

            // Separate provider: sharing student keys does not change teacher/OIDC cookie protection.
            options.DataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(keyRingPath), protection =>
            {
                protection.SetApplicationName("Bifrost.StudentIdentity");
                if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
            });
        });
    }

    public static IServiceCollection AddBifrostServiceAuthentication(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddAuthentication("EndpointAuthentication")
            .AddPolicyScheme("EndpointAuthentication", null, options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Path.StartsWithSegments("/api/students")
                        ? StudentCookieHandler.SchemeName
                        : JwtBearerDefaults.AuthenticationScheme;
            })
            .AddStudentCookie(configuration)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Authentication:Authority"] ?? "http://localhost:6003";
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.Audience = "Bifrost";
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Student", policy => policy
                .AddAuthenticationSchemes(StudentCookieHandler.SchemeName)
                .RequireAuthenticatedUser().RequireClaim("sub").RequireRole("Student"));
            options.AddPolicy("Teacher", policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().RequireClaim("sub").RequireRole("Teacher")
                .RequireAssertion(context => context.User.FindAll("scope")
                    .Any(claim => claim.Value.Split(' ').Contains("Bifrost"))));
        });
        return services;
    }
}
