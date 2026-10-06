
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect(options =>
            {
                options.Authority = "http://localhost:6003";
                options.ClientId = "interactive";
                options.ClientSecret = "49C1A7E1-0C79-4A89-A3D6-A37998FB86B0";
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.RequireHttpsMetadata = false;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("roles");
                options.Scope.Add("scope1");
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Anonymous", policy => policy.RequireAssertion(_ => true));
            options.AddPolicy("Teacher", policy => policy
                .RequireAuthenticatedUser()
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

        app.MapGet("/account/login", () => Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/" },
            new[] { OpenIdConnectDefaults.AuthenticationScheme }));
        app.MapGet("/account/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
        });

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/students"))
            {
                const string cookieName = "bifrost-anonymous-user";
                if (!context.Request.Cookies.TryGetValue(cookieName, out var userId) ||
                    !Guid.TryParse(userId, out _))
                {
                    userId = Guid.NewGuid().ToString("N");
                    context.Response.Cookies.Append(cookieName, userId, new CookieOptions
                    {
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax,
                        IsEssential = true
                    });
                }

                context.Request.Headers["X-Bifrost-User-Id"] = userId;
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                var accessToken = await context.GetTokenAsync("access_token");
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    context.Request.Headers.Authorization = $"Bearer {accessToken}";
                }
            }

            await next();
        });

        app.MapReverseProxy();

        app.Run();
    }
}
