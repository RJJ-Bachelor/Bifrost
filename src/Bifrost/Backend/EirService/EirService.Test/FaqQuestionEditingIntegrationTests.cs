using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Application.Features.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task EditedFaqQuestionAndAnswerArePersistedTogetherAcrossConversationAndKnowledgeBase()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Physical);
        var generalized = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1,
            "Original shared question", [firstId, secondId]);
        await DeliverGeneralizationAsync(generalized);
        using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);

        using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{generalized.GeneralizationId}/messages",
            new { Text = " Shared answer ", Question = " Revised shared question " });

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        foreach (var (client, id) in new[] { (teacher, generalized.GeneralizationId), (student, firstId), (second, secondId) })
        {
            var role = client == teacher ? "teachers" : "students";
            var request = (await client.GetFromJsonAsync<HelpRequestResult>($"/api/{role}/requests/{id}", FaqJsonOptions))!;
            Assert.Equal("Revised shared question", Assert.Single(request.Messages,
                message => message.MessageType == HelpRequestMessageType.FaqQuestion).Text);
            Assert.Equal("Shared answer", request.Messages.Last().Text);
        }
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var outgoing = Assert.Single(await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox
                .Where(message => message.RoutingKey == MessageRoutes.FaqAnswered).ToArrayAsync());
            var payload = JsonSerializer.Deserialize<FaqAnsweredMessage>(outgoing.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal("Revised shared question", payload.Question);
            Assert.Equal(session.Id, payload.SessionId);
            Assert.Equal(1, payload.Generation);
        }

        await PersistFaqAnswerFromOutboxAsync();

        var faq = Assert.Single((await student.GetFromJsonAsync<FaqResult[]>($"/api/students/sessions/{session.Id}/faqs"))!);
        Assert.Equal("Revised shared question", faq.Question);
        Assert.Equal("Shared answer", faq.Answer);
        using var repeated = await teacher.PostAsJsonAsync($"/api/teachers/requests/{generalized.GeneralizationId}/messages",
            new { Text = "Different answer", Question = "Another question" });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
    }
}
