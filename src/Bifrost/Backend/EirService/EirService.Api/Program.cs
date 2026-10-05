
using EirService.Api.Extensions;
using EirService.Requests.Application.Features.Commands.CreateRequest;
using EirService.Requests.Infrastructure;
using EirService.Requests.Infrastructure.Persistence;
using FluentValidation;
using MediatR;
using Shared.Application.Behaviors;

namespace EirService.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddRequestsInfrastructure(builder.Configuration);

        // Add services to the container.
        builder.Services.AddAuthorization();
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

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.Run();
    }
}
