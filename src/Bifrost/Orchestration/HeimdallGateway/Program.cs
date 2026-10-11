using HeimdallGateway.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace HeimdallGateway;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        builder.Services.AddHeimdallAuthentication(builder.Configuration, builder.Environment);
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

        app.UseHeimdallAuthentication();

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

        app.MapReverseProxy();

        app.Run();
    }
}
