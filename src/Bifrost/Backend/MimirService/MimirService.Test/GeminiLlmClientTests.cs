using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimirService.Application.Services.Llm;
using MimirService.Infrastructure.Llm;

namespace MimirService.Test;

public sealed class GeminiLlmClientTests
{
    [Fact]
    public async Task FirstModelAnswerIsReturnedWithoutTryingTheNext()
    {
        var handler = new FakeGeminiHandler { ["model-a"] = Answer("Answer from A"), ["model-b"] = Answer("Answer from B") };

        var text = await Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", CancellationToken.None);

        Assert.Equal("Answer from A", text);
        Assert.Equal(new[] { "model-a" }, handler.Calls);
    }

    [Fact]
    public async Task FailedModelFallsBackToTheNext()
    {
        var handler = new FakeGeminiHandler
        {
            ["model-a"] = Json("{}", HttpStatusCode.InternalServerError),
            ["model-b"] = Answer("Answer from B")
        };

        var text = await Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", CancellationToken.None);

        Assert.Equal("Answer from B", text);
        Assert.Equal(new[] { "model-a", "model-b" }, handler.Calls);
    }

    [Fact]
    public async Task EmptyAnswerFallsBackToTheNext()
    {
        var handler = new FakeGeminiHandler { ["model-a"] = Answer(""), ["model-b"] = Answer("Answer from B") };

        var text = await Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", CancellationToken.None);

        Assert.Equal("Answer from B", text);
    }

    [Fact]
    public async Task InvalidJsonFallsBackToTheNext()
    {
        var handler = new FakeGeminiHandler { ["model-a"] = Json("not json"), ["model-b"] = Answer("Answer from B") };

        var text = await Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", CancellationToken.None);

        Assert.Equal("Answer from B", text);
    }

    [Fact]
    public async Task ThoughtPartsAreSkipped()
    {
        var handler = new FakeGeminiHandler
        {
            ["model-a"] = Json("""{"candidates":[{"content":{"parts":[{"text":"Thinking","thought":true},{"text":"Answer"}]}}]}""")
        };

        var text = await Client(handler, "model-a").GenerateAsync("system", "prompt", CancellationToken.None);

        Assert.Equal("Answer", text);
    }

    [Fact]
    public async Task AllModelsFailingThrowsLlmUnavailable()
    {
        var handler = new FakeGeminiHandler
        {
            ["model-a"] = Json("{}", HttpStatusCode.InternalServerError),
            ["model-b"] = Json("{}", HttpStatusCode.TooManyRequests)
        };

        await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", CancellationToken.None));
        Assert.Equal(new[] { "model-a", "model-b" }, handler.Calls);
    }

    [Fact]
    public async Task CallerCancellationIsNotTreatedAsModelFailure()
    {
        var handler = new FakeGeminiHandler { ["model-a"] = Answer("Answer from A"), ["model-b"] = Answer("Answer from B") };
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", cancellation.Token));
        Assert.DoesNotContain("model-b", handler.Calls);
    }

    private static GeminiLlmClient Client(FakeGeminiHandler handler, params string[] models) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://gemini.test/") },
            Options.Create(new GeminiOptions { ApiKey = "test-key", Models = models }),
            NullLogger<GeminiLlmClient>.Instance);

    private static Func<HttpResponseMessage> Answer(string text) =>
        Json($$$"""{"candidates":[{"content":{"parts":[{"text":"{{{text}}}"}]}}]}""");

    private static Func<HttpResponseMessage> Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeGeminiHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> responses = [];

        public List<string> Calls { get; } = [];

        public Func<HttpResponseMessage> this[string model]
        {
            set => responses[model] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            var start = path.IndexOf("models/", StringComparison.Ordinal) + "models/".Length;
            var model = path[start..path.IndexOf(':', start)];
            Calls.Add(model);
            return Task.FromResult(responses.TryGetValue(model, out var response)
                ? response()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
