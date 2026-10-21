using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimirService.Api.Endpoints.Test;
using MimirService.Application.Services.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Test;

public sealed class GeneralizationTestApiTests
{
    [Theory]
    [InlineData("Development", HttpStatusCode.Accepted)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task PublishesThroughTheApplicationContractOnlyInDevelopment(string environment, HttpStatusCode expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        var publisher = new RecordingPublisher();
        builder.Services.AddSingleton<IGeneralizedMessagePublisher>(publisher);
        await using var app = builder.Build();
        app.MapGeneralizationTestApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var id = Guid.NewGuid();
        var sourceIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        using var response = await client.PostAsJsonAsync("/api/test/generalizations",
            new { GeneralizationId = id, SessionId = Guid.NewGuid(), Generation = 2, Question = " Shared question ", HelpRequestIds = sourceIds });
        Assert.Equal(expected, response.StatusCode);
        if (environment == "Development")
        {
            var payload = Assert.Single(publisher.Messages);
            Assert.Equal(id, payload.GeneralizationId);
            Assert.Equal("Shared question", payload.Question);
            Assert.Equal(sourceIds.Order(), payload.HelpRequestIds);
            using var invalid = await client.PostAsJsonAsync("/api/test/generalizations",
                new { SessionId = Guid.NewGuid(), Generation = 2, Question = "Question", HelpRequestIds = new[] { sourceIds[0] } });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Single(publisher.Messages);
        }
        else Assert.Empty(publisher.Messages);
    }

    private sealed class RecordingPublisher : IGeneralizedMessagePublisher
    {
        public List<GeneralizedCreatedMessage> Messages { get; } = [];
        public Task PublishAsync(GeneralizedCreatedMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
