using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimirService.Infrastructure.Persistence;

namespace Backend.IntegrationTests.Scenarios;

[Collection("Backend hosts")]
public sealed class GeneralizationScenarioTests
{
    [ReportScenarioFact]
    [Trait("Category", "ExternalIntegration")]
    public async Task RelatedRequestsTravelThroughMimirAndRabbitMqAndBecomeOneFaqInEir()
    {
        await using var fixture = await ReportScenarioFixture.CreateAsync(withMimir: true);
        using var teacher = fixture.Teacher();
        using var firstStudent = fixture.Student();
        using var secondStudent = fixture.Student();
        var session = await CreateSessionAsync(teacher, fixture.Token);
        await JoinAsync(firstStudent, session.Code, fixture.Token);
        await JoinAsync(secondStudent, session.Code, fixture.Token);

        var firstId = await SubmitAsync(firstStudent, session.Id,
            "How do I solve the first part of the shared problem?", fixture.Token);
        // The first request must be committed in Mimir before the second is offered as a match.
        await WaitForMimirRequestAsync(fixture, firstId);
        Assert.Equal(0, fixture.LlmCalls);
        var secondId = await SubmitAsync(secondStudent, session.Id,
            "I am stuck on the same shared problem. How should I proceed?", fixture.Token);
        var sourceIds = new[] { firstId, secondId }.Order().ToArray();

        await ReportScenarioFixture.WaitUntilAsync(async () =>
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var knowledgeBase = scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>();
            var requests = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            return await knowledgeBase.Faqs.AnyAsync(faq => faq.SessionId == session.Id, fixture.Token)
                && await requests.Requests.CountAsync(request => sourceIds.Contains(request.Id)
                    && request.Status == HelpRequestStatus.AwaitingFaq, fixture.Token) == 2;
        }, fixture.Token);

        await using var verification = fixture.Services.CreateAsyncScope();
        var eirFaq = Assert.Single(await verification.ServiceProvider
            .GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.AsNoTracking().ToArrayAsync(fixture.Token));
        Assert.Equal(session.Id, eirFaq.SessionId);
        Assert.Equal(session.CurrentGeneration, eirFaq.Generation);
        Assert.Equal(fixture.GeneralizedQuestion, eirFaq.Question);
        Assert.Equal(sourceIds, eirFaq.HelpRequestIds.Order());
        Assert.Null(eirFaq.Answer);

        var eirRequests = verification.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var sources = await eirRequests.Requests.AsNoTracking().Include(request => request.Messages)
            .Where(request => sourceIds.Contains(request.Id)).ToArrayAsync(fixture.Token);
        Assert.Equal(2, sources.Length);
        Assert.All(sources, request =>
        {
            Assert.Equal(session.Id, request.SessionId);
            Assert.Equal(session.CurrentGeneration, request.Generation);
            Assert.Equal(HelpRequestStatus.AwaitingFaq, request.Status);
            Assert.Equal(eirFaq.Id, request.FaqAnswerId);
            var question = Assert.Single(request.Messages,
                message => message.MessageType == HelpRequestMessageType.FaqQuestion);
            Assert.Equal(fixture.GeneralizedQuestion, question.Text.Value);
        });
        var queuedFaq = await eirRequests.Requests.AsNoTracking()
            .SingleAsync(request => request.Id == eirFaq.Id, fixture.Token);
        Assert.True(queuedFaq.IsFaq);
        Assert.Equal(HelpRequestStatus.Pending, queuedFaq.Status);

        var mimir = verification.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
        var generalization = Assert.Single(await mimir.Generalizations.AsNoTracking().ToArrayAsync(fixture.Token));
        Assert.Equal(eirFaq.Id, generalization.Id);
        Assert.Equal(session.Id, generalization.SessionId);
        Assert.Equal(session.CurrentGeneration, generalization.Generation);
        Assert.Equal(fixture.GeneralizedQuestion, generalization.Question);
        var members = await mimir.Requests.AsNoTracking()
            .Where(request => request.GeneralizationId == generalization.Id)
            .Select(request => request.Id).ToArrayAsync(fixture.Token);
        Assert.Equal(sourceIds, members.Order());
        Assert.True(fixture.LlmCalls >= 1, "Mimir must use the ILlmClient stub to decide the generalization.");
    }

    [ReportScenarioFact]
    [Trait("Category", "ExternalIntegration")]
    public async Task IdenticalQuestionsInDifferentSessionsOrGenerationsRemainSeparate()
    {
        await using var fixture = await ReportScenarioFixture.CreateAsync(withMimir: true);
        using var teacher = fixture.Teacher();
        using var firstStudent = fixture.Student();
        using var secondStudent = fixture.Student();
        var firstSession = await CreateSessionAsync(teacher, fixture.Token);
        var secondSession = await CreateSessionAsync(teacher, fixture.Token);
        await JoinAsync(firstStudent, firstSession.Code, fixture.Token);
        await JoinAsync(secondStudent, secondSession.Code, fixture.Token);
        const string question = "How do I solve the shared problem?";

        var firstId = await SubmitAsync(firstStudent, firstSession.Id, question, fixture.Token);
        await WaitForMimirRequestAsync(fixture, firstId);
        var otherSessionId = await SubmitAsync(secondStudent, secondSession.Id, question, fixture.Token);
        await WaitForMimirRequestAsync(fixture, otherSessionId);

        using var cleared = await teacher.PostAsJsonAsync($"/api/teachers/sessions/{firstSession.Id}/clear",
            new { ExpectedGeneration = firstSession.CurrentGeneration }, fixture.Token);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var clearResult = await cleared.Content.ReadFromJsonAsync<JsonElement>(fixture.Token);
        var generation = clearResult.GetProperty("currentGeneration").GetInt32();
        Assert.Equal(firstSession.CurrentGeneration + 1, generation);
        var nextGenerationId = await SubmitAsync(firstStudent, firstSession.Id, question, fixture.Token);
        await WaitForMimirRequestAsync(fixture, nextGenerationId);

        // Persisted inputs prove consumption completed; no time-based absence assertion is needed.
        await using var verification = fixture.Services.CreateAsyncScope();
        var mimir = verification.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
        var requests = await mimir.Requests.AsNoTracking().ToArrayAsync(fixture.Token);
        Assert.Equal(3, requests.Length);
        Assert.All(requests, request => Assert.Null(request.GeneralizationId));
        var current = Assert.Single(requests, request => request.Id == nextGenerationId);
        Assert.Equal(firstSession.Id, current.SessionId);
        Assert.Equal(generation, current.Generation);
        Assert.Empty(await mimir.Generalizations.AsNoTracking().ToArrayAsync(fixture.Token));
        Assert.Empty(await verification.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>()
            .Faqs.AsNoTracking().ToArrayAsync(fixture.Token));
        Assert.Equal(0, fixture.LlmCalls);
    }

    private static async Task<SessionResult> CreateSessionAsync(HttpClient teacher, CancellationToken token)
    {
        using var response = await teacher.PostAsJsonAsync("/api/teachers/sessions",
            new { Title = "Generalization integration scenario" }, token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionResult>(token))!;
    }

    private static async Task JoinAsync(HttpClient student, string code, CancellationToken token)
    {
        using var response = await student.PostAsJsonAsync("/api/students/sessions/join", new { Code = code }, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> SubmitAsync(HttpClient student, Guid sessionId, string question, CancellationToken token)
    {
        using var response = await student.PostAsJsonAsync($"/api/students/sessions/{sessionId}/requests",
            new { Message = question, HelpType = "Digital" }, token);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Creating a request returned {response.StatusCode}: {await response.Content.ReadAsStringAsync(token)}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("id").GetGuid();
    }

    private static Task WaitForMimirRequestAsync(ReportScenarioFixture fixture, Guid id) =>
        ReportScenarioFixture.WaitUntilAsync(async () =>
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<MimirRequestDbContext>()
                .Requests.AnyAsync(request => request.Id == id, fixture.Token);
        }, fixture.Token);
}
