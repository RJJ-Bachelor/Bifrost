using Shared.Api.Authentication;
using MimirService.Infrastructure;
using MimirService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MimirService.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddMimirInfrastructure(builder.Configuration);

        // Add services to the container.
        builder.Services.AddBifrostServiceAuthentication(builder.Configuration, builder.Environment);

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<MimirRequestDbContext>();
            dbContext.Database.Migrate();
        }

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();

        app.Run();
    }
}
