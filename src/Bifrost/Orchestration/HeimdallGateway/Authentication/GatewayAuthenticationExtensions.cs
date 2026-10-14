using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Shared.Api.Authentication;

namespace HeimdallGateway.Authentication;

public static class GatewayAuthenticationExtensions
{
    public static IServiceCollection AddHeimdallAuthentication(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "GatewayAuthentication";
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddPolicyScheme("GatewayAuthentication", null, options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Path.StartsWithSegments("/api/students")
                        ? StudentCookieHandler.SchemeName
                        : "TeacherAuthentication";
            })
            .AddStudentCookie(configuration)
            .AddPolicyScheme("TeacherAuthentication", null, options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                    NotificationHubAuthentication.HasQueryToken(context.Request)
                        ? JwtBearerDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "bifrost-teacher";
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Authentication:Authority"] ?? "http://localhost:6003";
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.Audience = "Bifrost";
                options.MapInboundClaims = false;
                options.Events.OnMessageReceived = NotificationHubAuthentication.ReadQueryToken;
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = configuration["Authentication:Authority"] ?? "http://localhost:6003";
                options.ClientId = "interactive";
                options.ClientSecret = "49C1A7E1-0C79-4A89-A3D6-A37998FB86B0";
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.ClaimActions.MapJsonKey("role", "role");
                options.ResponseMode = "query";
                if (environment.IsDevelopment())
                {
                    options.NonceCookie.SameSite = SameSiteMode.Lax;
                    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                    options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                }
                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.HandleResponse();
                    }
                    return Task.CompletedTask;
                };
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("roles");
                options.Scope.Add("Bifrost");
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
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .RequireRole("Student"));
            options.AddPolicy("Teacher", policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .RequireRole("Teacher"));
        });
        return services;
    }

    public static WebApplication UseHeimdallAuthentication(this WebApplication app)
    {
        app.UseMiddleware<StudentIdentityMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();

        app.Use(async (context, next) =>
        {
            // Services validate the forwarded student cookie or VarService JWT themselves.
            context.Request.Headers.Remove("X-Bifrost-User-Id");
            context.Request.Headers.Remove("X-Bifrost-Roles");

            if (NotificationHubAuthentication.HasQueryToken(context.Request) &&
                context.User.Identity?.IsAuthenticated == true &&
                !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Request.Headers.Authorization = $"Bearer {context.Request.Query["access_token"]}";
                context.Request.QueryString = QueryString.Create(context.Request.Query
                    .Where(parameter => parameter.Key != "access_token"));
            }

            if (context.Request.Path.StartsWithSegments("/api/teachers") &&
                context.User.Identity?.IsAuthenticated == true &&
                !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var accessToken = await context.GetTokenAsync("access_token");
                var expiresAt = await context.GetTokenAsync("expires_at");
                if (string.IsNullOrWhiteSpace(accessToken) ||
                    !DateTimeOffset.TryParse(expiresAt, out var expiry) || expiry <= DateTimeOffset.UtcNow)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                context.Request.Headers.Authorization = $"Bearer {accessToken}";
            }

            await next();
        });

        return app;
    }
}
