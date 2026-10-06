
using EirService.Api.Extensions;
using EirService.Requests.Application.Features.Commands.CreateRequest;
using EirService.Requests.Infrastructure;
using EirService.Requests.Infrastructure.Persistence;
using FluentValidation;
using MediatR;
using Shared.Application.Behaviors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace EirService.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddRequestsInfrastructure(builder.Configuration);

        // Add services to the container.
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = "http://localhost:6003";
                options.RequireHttpsMetadata = false;
                options.Audience = "scope1";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Teacher", policy => policy
                .RequireAuthenticatedUser()
                .RequireRole("Teacher"));
        });
        builder.Services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssemblyContaining<CreateRequestCommandHandler>();
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        builder.Services.AddTransient<IValidator<CreateRequestCommand>, CreateRequestCommandValidator>();
        builder.Services.AddTransient<Endpoints.Student.Request>();

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            dbContext.Database.EnsureCreated();
        }

        app.MapEndpoints();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/students") &&
                !context.Request.Headers.ContainsKey("X-Bifrost-User-Id"))
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

            await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();

        app.Run();
    }
}
