using EirService.Api.Authentication;
using EirService.Api.Middleware;
using EirService.Api.Middleware.Commands;
using EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;
using EirService.HelpRequests.Application.Features.Student.GetHelpRequests;
using EirService.HelpRequests.Infrastructure;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Features.Teacher.ClearSession;
using EirService.Sessions.Application.Features.Teacher.GetSession;
using EirService.Sessions.Application.Features.Teacher.GetSessions;
using EirService.Sessions.Application.Features.Student.GetStudentSession;
using EirService.Sessions.Infrastructure;
using EirService.Sessions.Infrastructure.Persistence;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Infrastructure;
using EirService.Participants.Infrastructure.Persistence;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
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
        builder.Services.AddSessionsInfrastructure(builder.Configuration);
        builder.Services.AddParticipantsInfrastructure(builder.Configuration);

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
        builder.Services.AddFastEndpoints(options =>
        {
            options.Assemblies =
            [
                typeof(CreateSessionCommandHandler).Assembly,
                typeof(CreateHelpRequestCommandHandler).Assembly,
                typeof(JoinSessionCommandHandler).Assembly
            ];
        });
        builder.Services.AddTransient<IValidator<CreateHelpRequestCommand>, CreateHelpRequestCommandValidator>();
        builder.Services.AddTransient<IValidator<GetHelpRequestsQuery>, GetHelpRequestsQueryValidator>();
        builder.Services.AddTransient<IValidator<CreateSessionCommand>, CreateSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<ClearSessionCommand>, ClearSessionCommandValidator>();
        builder.Services.AddTransient<IValidator<GetSessionsQuery>, GetSessionsQueryValidator>();
        builder.Services.AddTransient<IValidator<GetSessionQuery>, GetSessionQueryValidator>();
        builder.Services.AddTransient<IValidator<GetStudentSessionQuery>, GetStudentSessionQueryValidator>();
        builder.Services.AddTransient<IValidator<JoinSessionCommand>, JoinSessionCommandValidator>();
        builder.Services.AddCommandMiddleware(options => options.Register(
            typeof(CommandValidationMiddleware<,>),
            typeof(CommandLoggingMiddleware<,>)));

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
            scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Database.Migrate();
            scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Database.Migrate();
            scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Database.Migrate();
        }

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseMiddleware<ValidationExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints(configuration => configuration.Endpoints.RoutePrefix = "api");

        app.Run();
    }
}
