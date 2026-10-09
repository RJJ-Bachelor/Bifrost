
using EirService.Api.Extensions;
using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Commands.CreateRequest;
using EirService.HelpRequests.Infrastructure;
using EirService.HelpRequests.Infrastructure.Persistence;
using FluentValidation;
using MediatR;
using Shared.Application.Behaviors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

namespace EirService.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddDataProtection();
        builder.Services.AddHelpRequestsInfrastructure(builder.Configuration);

        // Add services to the container.
        builder.Services
            .AddAuthentication("EndpointAuthentication")
            .AddPolicyScheme("EndpointAuthentication", null, options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Path.StartsWithSegments("/api/students")
                        ? StudentCookieHandler.SchemeName
                        : JwtBearerDefaults.AuthenticationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, StudentCookieHandler>(StudentCookieHandler.SchemeName, null)
            .AddJwtBearer(options =>
            {
                options.Authority = builder.Configuration["Authentication:Authority"] ?? "http://localhost:6003";
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                options.Audience = "Bifrost";
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Student", policy => policy
                .AddAuthenticationSchemes(StudentCookieHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .RequireRole("Student"));
            options.AddPolicy("Teacher", policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .RequireRole("Teacher")
                .RequireAssertion(context => context.User.FindAll("scope")
                    .Any(claim => claim.Value.Split(' ').Contains("Bifrost"))));
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
        // Accept the protocol forwarded by the local gateway so HTTPS cookies are Secure.
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto
        });

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            dbContext.Database.EnsureCreated();
            // EnsureCreated does not update existing databases. Preserve legacy requests,
            // whose owner was never recorded, and add ownership for all new requests.
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE requests ADD COLUMN IF NOT EXISTS "UserId" character varying(100) NULL;
                CREATE INDEX IF NOT EXISTS "IX_requests_UserId" ON requests ("UserId");
                """);
        }

        app.MapEndpoints();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.Run();
    }
}
