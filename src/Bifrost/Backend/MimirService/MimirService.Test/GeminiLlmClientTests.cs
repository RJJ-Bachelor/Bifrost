using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimirService.Application.Services.Llm;
using MimirService.Infrastructure.Llm;

namespace MimirService.Test;

public sealed class GeminiLlmClientTests
{
    private const string AnswerA = """{"decision":"standalone"}""";
    private const string AnswerB = """{"decision":"pair"}""";

    private static readonly JsonElement Schema = JsonSerializer.Deserialize<JsonElement>(
        """{"type":"object","properties":{"decision":{"type":"string","enum":["standalone","join","pair"]}},"required":["decision"]}""");

    [Fact]
    public async Task FirstModelAnswerIsReturnedWithoutTryingTheNext()
    {
        var handler = new FakeGeminiHandler { ["model-a"] = Answer(AnswerA), ["model-b"] = Answer(AnswerB) };

        var text = await Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", Schema, CancellationToken.None);

        Assert.Equal(AnswerA, text);
        Assert.Equal(new[] { "model-a" }, handler.Calls);
    }

    [Fact]
    public async Task FailedModelFallsBackToTheNext()
    {
        var handler = new FakeGeminiHandler
        {
            ["model-a"] = Json("{}", HttpStatusCode.InternalServerError),
            ["model-b"] = Answer(AnswerB)
        };

        var text = await Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", Schema, CancellationToken.None);

        Assert.Equal(AnswerB, text);
        Assert.Equal(new[] { "model-a", "model-b" }, handler.Calls);
    }

    [Fact]
    public async Task EmptyAnswerFallsBackToTheNext()
    {
        var handler = new FakeGeminiHandler { ["model-a"] = Answer(""), ["model-b"] = Answer(AnswerB) };

        var text = await Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", Schema, CancellationToken.None);

        Assert.Equal(AnswerB, text);
    }

    [Fact]
    public async Task InvalidResponseBodyFallsBackToTheNext()
    {
        var handler = new FakeGeminiHandler { ["model-a"] = Json("not json"), ["model-b"] = Answer(AnswerB) };

        var text = await Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", Schema, CancellationToken.None);

        Assert.Equal(AnswerB, text);
    }

    [Fact]
    public async Task ThoughtPartsAreSkipped()
    {
        var handler = new FakeGeminiHandler
        {
            ["model-a"] = Json("""{"candidates":[{"content":{"parts":[{"text":"Thinking","thought":true},{"text":"{\"decision\":\"standalone\"}"}]}}]}""")
        };

        var text = await Client(handler, "model-a").GenerateAsync("system", "prompt", Schema, CancellationToken.None);

        Assert.Equal(AnswerA, text);
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
            Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", Schema, CancellationToken.None));
        Assert.Equal(new[] { "model-a", "model-b" }, handler.Calls);
    }

    [Fact]
    public async Task CallerCancellationIsNotTreatedAsModelFailure()
    {
        var handler = new FakeGeminiHandler { ["model-a"] = Answer(AnswerA), ["model-b"] = Answer(AnswerB) };
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", Schema, cancellation.Token));
        Assert.DoesNotContain("model-b", handler.Calls);
    }

    [Fact]
    public async Task SchemaIsSentAsResponseFormat()
    {
        var handler = new FakeGeminiHandler { ["model-a"] = Answer(AnswerA) };

        var text = await Client(handler, "model-a").GenerateAsync("system", "prompt", Schema, CancellationToken.None);

        Assert.Equal(AnswerA, text);
        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var format = body.RootElement.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("text");
        Assert.Equal("APPLICATION_JSON", format.GetProperty("mimeType").GetString());
        Assert.True(JsonElement.DeepEquals(Schema, format.GetProperty("schema")));
    }

    [Fact]
    public async Task MalformedJsonAnswerFallsBackToTheNext()
    {
        var handler = new FakeGeminiHandler { ["model-a"] = Answer("""{"decision":"""), ["model-b"] = Answer(AnswerB) };

        var text = await Client(handler, "model-a", "model-b").GenerateAsync("system", "prompt", Schema, CancellationToken.None);

        Assert.Equal(AnswerB, text);
        Assert.Equal(new[] { "model-a", "model-b" }, handler.Calls);
    }

    private static GeminiLlmClient Client(FakeGeminiHandler handler, params string[] models) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://gemini.test/") },
            Options.Create(new GeminiOptions { ApiKey = "test-key", Models = models }),
            NullLogger<GeminiLlmClient>.Instance);

    // Serialized rather than interpolated, so answers containing JSON are escaped correctly.
    private static Func<HttpResponseMessage> Answer(string text) =>
        Json(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } } } } }));

    private static Func<HttpResponseMessage> Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeGeminiHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> responses = [];

        public List<string> Calls { get; } = [];

        public List<string> Bodies { get; } = [];

        public Func<HttpResponseMessage> this[string model]
        {
            set => responses[model] = value;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            var start = path.IndexOf("models/", StringComparison.Ordinal) + "models/".Length;
            var model = path[start..path.IndexOf(':', start)];
            Calls.Add(model);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return responses.TryGetValue(model, out var response)
                ? response()
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
