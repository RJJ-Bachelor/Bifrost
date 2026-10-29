using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    [Fact]
    public async Task AnswerEndpointRevisesFaqAndOnlyItsCurrentWaitingSources()
    {
        Authenticate("Teacher", "teacher-123");
        var sessionId = await SeedSessionAsync("student");
        var now = DateTimeOffset.UtcNow;
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), sessionId, 1, new MessageText("Original shared question"), now);
        faq.Reserve("teacher-123", now);
        var current = CreateLinkedSource("student", sessionId, 1);
        var cancelled = CreateLinkedSource("cancelled-student", sessionId, 1);
        cancelled.Cancel("cancelled-student", AuthorRole.Student, now);
        var oldGeneration = CreateLinkedSource("old-student", sessionId, 2);
        var otherSession = CreateLinkedSource("other-student", Guid.NewGuid(), 1);
        foreach (var request in new[] { faq, current, cancelled, oldGeneration, otherSession })
        {
            request.ClearDomainEvents();
            _requests.Saved.Add(request);
        }

        using var response = await _client.PostAsJsonAsync($"/api/teachers/requests/{faq.Id}/messages",
            new { Text = "Shared answer", Question = " Revised shared question " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Revised shared question", result.GetProperty("messages")[0].GetProperty("text").GetString());
        Assert.Equal("Shared answer", result.GetProperty("messages")[1].GetProperty("text").GetString());
        Assert.Equal(HelpRequestStatus.AwaitingConfirmation, current.Status);
        Assert.Equal("Revised shared question", current.Messages.Single(message => message.MessageType == HelpRequestMessageType.FaqQuestion).Text.Value);
        Assert.Equal("Private question", current.Messages.First().Text.Value);
        Assert.Equal(HelpRequestStatus.Cancelled, cancelled.Status);
        foreach (var untouched in new[] { cancelled, oldGeneration, otherSession })
        {
            Assert.Equal(2, untouched.Messages.Count);
            Assert.Equal("Original shared question", untouched.Messages.Last().Text.Value);
        }

        HelpRequest CreateLinkedSource(string studentId, Guid sourceSessionId, int generation)
        {
            var request = HelpRequest.Create(sourceSessionId, generation, studentId, HelpType.Digital,
                new MessageText("Private question"), now);
            request.LinkToFaq(faq.Id, new MessageText("Original shared question"), now);
            return request;
        }
    }

    [Fact]
    public async Task AnswerEndpointRejectsEditsToAnOrdinaryStudentRequestWithoutClosingIt()
    {
        Authenticate("Teacher", "teacher-123");
        var sessionId = await SeedSessionAsync("student");
        var request = HelpRequest.Create(sessionId, 1, "student", HelpType.Digital,
            new MessageText("Private question"), DateTimeOffset.UtcNow);
        request.Reserve("teacher-123", DateTimeOffset.UtcNow);
        request.ClearDomainEvents();
        _requests.Saved.Add(request);

        using var response = await _client.PostAsJsonAsync($"/api/teachers/requests/{request.Id}/messages",
            new { Text = "Answer", Question = "Edited private question" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Private question", Assert.Single(request.Messages).Text.Value);
        Assert.Equal(HelpRequestStatus.Pending, request.Status);
        Assert.Empty(request.GetDomainEvents());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task AnswerEndpointRejectsAnEmptyEditedQuestionBeforeAccessingTheRequest(string question)
    {
        Authenticate("Teacher", "teacher-123");

        using var response = await _client.PostAsJsonAsync($"/api/teachers/requests/{Guid.NewGuid()}/messages",
            new { Text = "Answer", Question = question });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Question", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_requests.Saved);
        Assert.Empty(_executionOrder);
    }
}
