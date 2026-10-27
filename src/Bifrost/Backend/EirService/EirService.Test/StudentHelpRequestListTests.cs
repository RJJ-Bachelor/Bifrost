using System.Net;
using System.Net.Http.Json;
using EirService.HelpRequests.Application.Features.Student.GetHelpRequests;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StudentListReturnsQueueTimesAndOrdersReopenedRequestsByTheirNewQueueTime(bool filterBySession)
    {
        var studentId = AuthenticateStudent();
        var sessionId = await SeedSessionAsync(studentId);
        var now = new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
        var reopened = HelpRequest.Create(sessionId, 1, studentId, HelpType.Digital, new MessageText("First question"), now);
        reopened.Reserve("teacher-123", now.AddMinutes(1));
        reopened.Answer("teacher-123", new MessageText("Answer"), now.AddMinutes(2));
        reopened.AddStudentMessage(studentId, new MessageText("Follow-up question"), now.AddMinutes(10));
        var first = HelpRequest.Create(sessionId, 1, studentId, HelpType.Physical, new MessageText("Waiting question"), now.AddMinutes(5));
        first.Reserve("teacher-123", now.AddMinutes(6));
        first.Start("teacher-123", now.AddMinutes(7));
        first.Complete("teacher-123", now.AddMinutes(8));
        var tied = HelpRequest.Create(sessionId, 1, studentId, HelpType.Digital, new MessageText("Another waiting question"), first.QueuedAt);
        tied.Reserve("teacher-123", now.AddMinutes(6));
        tied.Answer("teacher-123", new MessageText("Another answer"), now.AddMinutes(8));
        _requests.Saved.AddRange([reopened, tied, first]);

        var url = "/api/students/requests" + (filterBySession ? $"?sessionId={sessionId}" : "");
        using var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = (await response.Content.ReadFromJsonAsync<HelpRequestResult[]>())!;

        Assert.Equal(new[] { first, tied }.OrderBy(request => request.Id).Select(request => request.Id).Append(reopened.Id),
            results.Select(request => request.Id));
        Assert.Equal(new[] { first.QueuedAt, tied.QueuedAt, reopened.QueuedAt }, results.Select(request => request.QueuedAt));
        Assert.Equal("First question", results[^1].Message);
    }
}
