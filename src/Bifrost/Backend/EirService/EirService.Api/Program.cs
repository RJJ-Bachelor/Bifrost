
using EirService.Api.Extensions;
using EirService.Requests.Application.Behaviors;
using EirService.Requests.Application.Features.Commands.CreateRequest;
using FluentValidation;
using MediatR;

namespace EirService.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

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
