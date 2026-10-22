using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimirService.Api.Endpoints.Test;
using MimirService.Application;
using MimirService.Application.Features.Development.GeneralizeHelpRequests;
using MimirService.Application.Services.Messaging;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Test;

public sealed class GeneralizeHelpRequestsApiTests
{
    private const string Route = "/api/test/generalizations/from-help-requests";
    private const string ExpectedQuestion = "Dette er et generaliseret spørgsmål.";

    [Fact]
    public async Task PublishesUserCreatedRequestsWithoutDependingOnAMimirProjection()
    {
        await using var application = await TestApplication.StartAsync();
        var sessionId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        using var response = await application.Client.PostAsJsonAsync(Route, new
        {
            sessionId,
            generation = 7,
            helpRequestIds = new[] { second, first },
            question = "A caller cannot replace the development question."
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = Assert.IsType<GeneralizeHelpRequestsResult>(
            await response.Content.ReadFromJsonAsync<GeneralizeHelpRequestsResult>());
        Assert.NotEqual(Guid.Empty, result.GeneralizationId);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(7, result.Generation);
        Assert.Equal(ExpectedQuestion, result.Question);
        Assert.Equal(new[] { first, second }.Order(), result.HelpRequestIds);
        var published = Assert.Single(application.Publisher.Messages);
        Assert.Equal(result.GeneralizationId, published.GeneralizationId);
        Assert.Equal(sessionId, published.SessionId);
        Assert.Equal(7, published.Generation);
        Assert.Equal(ExpectedQuestion, published.Question);
        Assert.Equal(result.HelpRequestIds, published.HelpRequestIds);
    }

    [Fact]
    public async Task ExplicitGeneralizationIdCanBeReusedToPublishTheSameEvent()
    {
        await using var application = await TestApplication.StartAsync();
        var request = ValidCommand() with { GeneralizationId = Guid.NewGuid() };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await application.Client.PostAsJsonAsync(Route, request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<GeneralizeHelpRequestsResult>();
            Assert.Equal(request.GeneralizationId, result!.GeneralizationId);
        }

        Assert.Equal(2, application.Publisher.Messages.Count);
        var first = application.Publisher.Messages[0];
        var second = application.Publisher.Messages[1];
        Assert.Equal(request.GeneralizationId, first.GeneralizationId);
        Assert.Equal(first.GeneralizationId, second.GeneralizationId);
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal(first.Generation, second.Generation);
        Assert.Equal(first.Question, second.Question);
        Assert.Equal(first.HelpRequestIds, second.HelpRequestIds);
    }

    [Fact]
    public async Task AcceptsTheMaximumNumberOfDistinctSourceRequests()
    {
        await using var application = await TestApplication.StartAsync();
        var request = ValidCommand() with
        {
            HelpRequestIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray()
        };

        using var response = await application.Client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(100, Assert.Single(application.Publisher.Messages).HelpRequestIds.Length);
    }

    [Theory]
    [InlineData("missing-metadata")]
    [InlineData("empty-session")]
    [InlineData("zero-generation")]
    [InlineData("negative-generation")]
    [InlineData("null-sources")]
    [InlineData("empty-sources")]
    [InlineData("one-source")]
    [InlineData("one-distinct-source")]
    [InlineData("mixed-duplicate-sources")]
    [InlineData("too-many-sources")]
    [InlineData("too-many-raw-entries")]
    [InlineData("empty-source-id")]
    [InlineData("empty-generalization-id")]
    [InlineData("self-reference")]
    public async Task InvalidMetadataDoesNotPublish(string invalidCase)
    {
        await using var application = await TestApplication.StartAsync();
        var valid = ValidCommand();
        var first = valid.HelpRequestIds![0];
        var invalid = invalidCase switch
        {
            "missing-metadata" => new GeneralizeHelpRequestsCommand(Guid.Empty, 0, null),
            "empty-session" => valid with { SessionId = Guid.Empty },
            "zero-generation" => valid with { Generation = 0 },
            "negative-generation" => valid with { Generation = -1 },
            "null-sources" => valid with { HelpRequestIds = null },
            "empty-sources" => valid with { HelpRequestIds = [] },
            "one-source" => valid with { HelpRequestIds = [first] },
            "one-distinct-source" => valid with { HelpRequestIds = [first, first] },
            "mixed-duplicate-sources" => valid with { HelpRequestIds = [first, first, valid.HelpRequestIds[1]] },
            "too-many-sources" => valid with
            {
                HelpRequestIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()
            },
            "too-many-raw-entries" => valid with
            {
                HelpRequestIds = Enumerable.Range(0, 101).Select(index => valid.HelpRequestIds[index % 2]).ToArray()
            },
            "empty-source-id" => valid with { HelpRequestIds = [first, Guid.Empty] },
            "empty-generalization-id" => valid with { GeneralizationId = Guid.Empty },
            "self-reference" => valid with { GeneralizationId = first },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
        };

        using var response = await application.Client.PostAsJsonAsync(Route, invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(application.Publisher.Messages);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task IsUnavailableOutsideDevelopment(string environment)
    {
        await using var application = await TestApplication.StartAsync(environment);

        using var response = await application.Client.PostAsJsonAsync(Route, ValidCommand());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(application.Publisher.Messages);
    }

    [Fact]
    public async Task ApplicationHandlerValidatesBeforeInvokingThePublisher()
    {
        var publisher = new RecordingPublisher();
        var handler = new GeneralizeHelpRequestsCommandHandler(
            new GeneralizeHelpRequestsCommandValidator(), publisher);

        await Assert.ThrowsAsync<ValidationException>(() => handler.ExecuteAsync(
            ValidCommand() with { HelpRequestIds = null }, CancellationToken.None));

        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task PublisherFailureDoesNotReturnAnAcceptedResponse()
    {
        var failure = new InvalidOperationException("The outbox could not be committed.");
        await using var application = await TestApplication.StartAsync(publishFailure: failure);

        HttpResponseMessage? response = null;
        var thrown = await Record.ExceptionAsync(async () =>
        {
            response = await application.Client.PostAsJsonAsync(Route, ValidCommand());
        });

        if (thrown is null)
        {
            using (response)
                Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
        }
        else
        {
            Assert.Same(failure, thrown);
        }
        Assert.Empty(application.Publisher.Messages);
    }

    private static GeneralizeHelpRequestsCommand ValidCommand() =>
        new(Guid.NewGuid(), 3, [Guid.NewGuid(), Guid.NewGuid()]);

    private sealed class TestApplication(
        WebApplication app, HttpClient client, RecordingPublisher publisher) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public RecordingPublisher Publisher { get; } = publisher;

        public static async Task<TestApplication> StartAsync(
            string environment = "Development", Exception? publishFailure = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var publisher = new RecordingPublisher(publishFailure);
            builder.Services.AddSingleton<IGeneralizedMessagePublisher>(publisher);
            builder.Services.AddMimirDevelopmentFeatures();
            var app = builder.Build();
            app.MapGeneralizationTestApi();
            await app.StartAsync();
            return new TestApplication(app, app.GetTestClient(), publisher);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class RecordingPublisher(Exception? failure = null) : IGeneralizedMessagePublisher
    {
        public List<GeneralizedCreatedMessage> Messages { get; } = [];

        public Task PublishAsync(GeneralizedCreatedMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failure is not null) throw failure;
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
