using Microsoft.Extensions.Logging.Abstractions;
using MimirService.Application.Services.Generalizations;
using MimirService.Domain.Entities;

namespace MimirService.Test;

public sealed class GeneralizationDeciderTests
{
    private static readonly GeneralizationCandidate LoopRequest =
        new(Guid.NewGuid(), GeneralizationCandidateType.Request, "opg 3 min while løkke stopper aldrig");

    private static readonly GeneralizationCandidate LastElementGeneralization =
        new(Guid.NewGuid(), GeneralizationCandidateType.Generalization, "Hvorfor kommer det sidste element ikke med i løkken i opgave 2?");

    [Fact]
    public async Task NoCandidatesIsStandaloneWithoutCallingTheLlm()
    {
        var llm = new FakeLlmClient();

        var decision = await Decider(llm).DecideAsync("opgave 3 hænger", [], CancellationToken.None);

        Assert.Equal(GeneralizationDecision.Standalone, decision);
        Assert.Empty(llm.Prompts);
    }

    [Fact]
    public async Task PairReturnsTheRequestAndTheFaqQuestion()
    {
        var llm = new FakeLlmClient
        {
            Answer = """{"decision":"pair","candidateId":"r1","faqQuestion":" Hvorfor stopper løkken i opgave 3 aldrig? "}"""
        };

        var decision = await Decider(llm).DecideAsync("opgave 3 hænger", [LoopRequest, LastElementGeneralization], CancellationToken.None);

        Assert.Equal(new GeneralizationDecision(GeneralizationDecisionType.Pair, LoopRequest.Id, "Hvorfor stopper løkken i opgave 3 aldrig?"), decision);
    }

    [Fact]
    public async Task JoinReturnsTheGeneralization()
    {
        var llm = new FakeLlmClient { Answer = """{"decision":"join","candidateId":"g1","faqQuestion":null}""" };

        var decision = await Decider(llm).DecideAsync("opg 2 det sidste tal kommer aldrig med", [LoopRequest, LastElementGeneralization], CancellationToken.None);

        Assert.Equal(new GeneralizationDecision(GeneralizationDecisionType.Join, LastElementGeneralization.Id, null), decision);
    }

    [Theory]
    [InlineData("""{"decision":"pair","candidateId":"r9","faqQuestion":"Hvorfor?"}""")] // Unknown candidate
    [InlineData("""{"decision":"join","candidateId":"r1","faqQuestion":null}""")] // Join to a request
    [InlineData("""{"decision":"pair","candidateId":"g1","faqQuestion":"Hvorfor?"}""")] // Pair with a generalization
    [InlineData("""{"decision":"pair","candidateId":"r1","faqQuestion":" "}""")] // Pair without a FAQ question
    [InlineData("""{"decision":"merge","candidateId":"r1","faqQuestion":null}""")] // Unknown decision
    [InlineData("not json")]
    public async Task InvalidAnswerIsStandalone(string answer)
    {
        var decision = await Decider(new FakeLlmClient { Answer = answer })
            .DecideAsync("opgave 3 hænger", [LoopRequest, LastElementGeneralization], CancellationToken.None);

        Assert.Equal(GeneralizationDecision.Standalone, decision);
    }

    [Fact]
    public async Task TooLongFaqQuestionIsStandalone()
    {
        var faqQuestion = new string('a', Generalization.MaxQuestionLength + 1);
        var llm = new FakeLlmClient
        {
            Answer = $$"""{"decision":"pair","candidateId":"r1","faqQuestion":"{{faqQuestion}}"}"""
        };

        var decision = await Decider(llm).DecideAsync("opgave 3 hænger", [LoopRequest], CancellationToken.None);

        Assert.Equal(GeneralizationDecision.Standalone, decision);
    }

    [Fact]
    public async Task UnavailableLlmIsStandalone()
    {
        var decision = await Decider(new FakeLlmClient { Unavailable = true })
            .DecideAsync("opgave 3 hænger", [LoopRequest], CancellationToken.None);

        Assert.Equal(GeneralizationDecision.Standalone, decision);
    }

    [Fact]
    public async Task PromptUsesShortIdsAndKeepsDanishLetters()
    {
        var llm = new FakeLlmClient();

        await Decider(llm).DecideAsync("hvorfor kører min løkke?", [LoopRequest, LastElementGeneralization], CancellationToken.None);

        var prompt = Assert.Single(llm.Prompts);
        Assert.Contains("\"id\":\"r1\"", prompt);
        Assert.Contains("\"id\":\"g1\"", prompt);
        Assert.DoesNotContain(LoopRequest.Id.ToString(), prompt);
        Assert.Contains("løkke", prompt);
    }

    private static GeneralizationDecider Decider(FakeLlmClient llm) =>
        new(llm, NullLogger<GeneralizationDecider>.Instance);
}
