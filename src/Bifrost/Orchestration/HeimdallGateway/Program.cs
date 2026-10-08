
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HeimdallGateway;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "TeacherAuthentication";
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddPolicyScheme("TeacherAuthentication", null, options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
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
                options.Authority = builder.Configuration["Authentication:Authority"] ?? "http://localhost:6003";
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                options.Audience = "Bifrost";
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "role";
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = builder.Configuration["Authentication:Authority"] ?? "http://localhost:6003";
                options.ClientId = "interactive";
                options.ClientSecret = "49C1A7E1-0C79-4A89-A3D6-A37998FB86B0";
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.ClaimActions.MapJsonKey("role", "role");
                options.ResponseMode = "query";
                if (builder.Environment.IsDevelopment())
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

        builder.Services.AddAuthorization(options =>
        {
            // YARP reserves "Anonymous" for routes that allow access without login.
            options.AddPolicy("Teacher", policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .RequireRole("Teacher"));
        });
        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        string LoginReturnUrl(string? returnUrl)
        {
            var frontends = builder.Configuration.GetSection("Frontend:AdditionalUrls").Get<string[]>() ?? [];
            if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var requested) &&
                string.IsNullOrEmpty(requested.UserInfo) &&
                frontends.Append(builder.Configuration["Frontend:Url"]).Any(frontend =>
                    Uri.TryCreate(frontend, UriKind.Absolute, out var allowedOrigin) &&
                    requested.Scheme == allowedOrigin.Scheme && requested.Authority == allowedOrigin.Authority))
            {
                return requested.AbsoluteUri;
            }
            return "/api/teachers/me";
        }

        app.MapGet("/account/login", (string? returnUrl) => Results.Challenge(
            new AuthenticationProperties { RedirectUri = LoginReturnUrl(returnUrl) },
            new[] { OpenIdConnectDefaults.AuthenticationScheme }));
        app.MapGet("/account/logout", async (HttpContext context, string? returnUrl) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = LoginReturnUrl(returnUrl) });
        });

        app.Use(async (context, next) =>
        {
            // Identity comes from the student cookie or a validated VarService JWT in Eir.
            context.Request.Headers.Remove("X-Bifrost-User-Id");
            context.Request.Headers.Remove("X-Bifrost-Roles");

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

        app.MapReverseProxy();

        app.Run();
    }
}
