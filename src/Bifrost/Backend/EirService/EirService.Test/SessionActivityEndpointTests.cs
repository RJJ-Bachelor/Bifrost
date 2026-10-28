using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    [Fact]
    public async Task SessionStatisticsCountAllCompletionOutcomesInCurrentGenerationAndEachStudentOnce()
    {
        var session = await new CreateSessionCommand("owner", "Statistics").ExecuteAsync();
        var storedSession = (await _app.Services.GetRequiredService<ISessionRepository>().GetAsync(session.Id, default))!;
        storedSession.Clear(1, DateTimeOffset.UtcNow);
        var queuedAt = DateTimeOffset.UtcNow.AddHours(-1);
        _requests.Saved.AddRange([
            CompletedActivity(session.Id, 2, "same-student", HelpType.Physical, queuedAt, 20),
            CompletedActivity(session.Id, 2, "same-student", HelpType.Physical, queuedAt, 10),
            CompletedActivity(session.Id, 2, "other-student", HelpType.Digital, queuedAt, 6),
            CompletedFaqActivity(session.Id, 2, queuedAt, 4),
            AcceptedFaqActivity(session.Id, 2, "faq-student", HelpType.Physical, queuedAt, 8),
            AcceptedFaqActivity(session.Id, 2, "same-student", HelpType.Digital, queuedAt, 12),
            CompletedActivity(session.Id, 1, "old-student", HelpType.Physical, queuedAt, 100),
            CompletedActivity(Guid.NewGuid(), 2, "unrelated-student", HelpType.Digital, queuedAt, 100),
            PendingActivity(session.Id, 2, "waiting-student", HelpType.Digital, queuedAt)
        ]);
        Authenticate("Teacher", "owner");

        var statistics = await ReadActivityAsync($"/api/teachers/sessions/{session.Id}/statistics");

        Assert.Equal(3, statistics.GetProperty("physicalCount").GetInt32());
        Assert.Equal(3, statistics.GetProperty("digitalCount").GetInt32());
        Assert.Equal(3, statistics.GetProperty("helpedStudentCount").GetInt32());
        Assert.Equal(10d, statistics.GetProperty("averageWaitMinutes").GetDouble());
        AssertProperties(statistics, "physicalCount", "digitalCount", "helpedStudentCount", "averageWaitMinutes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionStatisticsReturnZeroCountsAndNullAverageWithoutCompletedRequests(bool includeUnfinishedRequests)
    {
        var session = await new CreateSessionCommand("owner", "Unfinished").ExecuteAsync();
        if (includeUnfinishedRequests)
        {
            var queuedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
            var pending = PendingActivity(session.Id, 1, "waiting", HelpType.Digital, queuedAt);
            var active = PendingActivity(session.Id, 1, "active", HelpType.Physical, queuedAt);
            active.Reserve("owner", queuedAt);
            active.Start("owner", queuedAt.AddMinutes(2));
            var cancelled = PendingActivity(session.Id, 1, "cancelled", HelpType.Digital, queuedAt);
            cancelled.Cancel("cancelled", AuthorRole.Student, queuedAt.AddMinutes(5));
            var awaitingFaq = PendingActivity(session.Id, 1, "awaiting-faq", HelpType.Digital, queuedAt);
            awaitingFaq.LinkToFaq(Guid.NewGuid(), new MessageText("Generalized question"), queuedAt);
            var awaitingConfirmation = PendingActivity(session.Id, 1, "awaiting-confirmation", HelpType.Physical, queuedAt);
            var faqId = Guid.NewGuid();
            awaitingConfirmation.LinkToFaq(faqId, new MessageText("Generalized question"), queuedAt);
            awaitingConfirmation.SuggestFaq(faqId, "owner", new MessageText("Suggested answer"), queuedAt.AddMinutes(5));
            _requests.Saved.AddRange([pending, active, cancelled, awaitingFaq, awaitingConfirmation]);
        }
        Authenticate("Teacher", "owner");

        var statistics = await ReadActivityAsync($"/api/teachers/sessions/{session.Id}/statistics");

        Assert.Equal(0, statistics.GetProperty("physicalCount").GetInt32());
        Assert.Equal(0, statistics.GetProperty("digitalCount").GetInt32());
        Assert.Equal(0, statistics.GetProperty("helpedStudentCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, statistics.GetProperty("averageWaitMinutes").ValueKind);
    }

    [Fact]
    public async Task StudentQueueStatusEstimatesBeforeSubmittingAndUsesExistingServiceDuration()
    {
        var studentId = AuthenticateStudent();
        var session = await new CreateSessionCommand("owner", "Before submitting").ExecuteAsync();
        await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
        var queuedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var completed = PendingActivity(session.Id, 1, "helped", HelpType.Physical, queuedAt);
        completed.Reserve("owner", queuedAt.AddMinutes(10));
        completed.Start("owner", queuedAt.AddMinutes(10));
        completed.Complete("owner", queuedAt.AddMinutes(14.2));
        var reserved = PendingActivity(session.Id, 1, "reserved", HelpType.Physical, queuedAt);
        reserved.Reserve("owner", queuedAt.AddMinutes(2));
        var active = PendingActivity(session.Id, 1, "active", HelpType.Physical, queuedAt);
        active.Reserve("owner", queuedAt);
        active.Start("owner", queuedAt.AddMinutes(2));
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), session.Id, 1, new MessageText("FAQ question"), queuedAt);
        _requests.Saved.AddRange([completed, reserved, active, faq]);

        var status = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue/status");
        var ownQueue = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue");

        Assert.Equal(2, status.GetProperty("waitingCount").GetInt32());
        Assert.Equal(13, status.GetProperty("estimatedWaitMinutes").GetInt32());
        Assert.Equal(JsonValueKind.Null, ownQueue.GetProperty("estimatedWaitMinutes").ValueKind);
        AssertProperties(status, "waitingCount", "estimatedWaitMinutes");
    }

    [Theory]
    [InlineData(HelpType.Physical)]
    [InlineData(HelpType.Digital)]
    public async Task StudentQueueEstimateBecomesAvailableAfterThirtySecondCompletion(HelpType helpType)
    {
        var studentId = AuthenticateStudent();
        var session = await new CreateSessionCommand("owner", "Short help duration").ExecuteAsync();
        await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
        var queuedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var completed = PendingActivity(session.Id, 1, "helped", helpType, queuedAt);
        var waiting = PendingActivity(session.Id, 1, studentId, helpType, queuedAt.AddSeconds(1));
        _requests.Saved.AddRange([completed, waiting]);

        var initialStatus = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue/status");
        Assert.Equal(2, initialStatus.GetProperty("waitingCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, initialStatus.GetProperty("estimatedWaitMinutes").ValueKind);

        var startedAt = queuedAt.AddMinutes(9);
        completed.Reserve("owner", startedAt);
        if (helpType == HelpType.Physical)
        {
            completed.Start("owner", startedAt);
            completed.Complete("owner", startedAt.AddSeconds(30));
        }
        else completed.Answer("owner", new MessageText("Answer"), startedAt.AddSeconds(30));

        var status = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue/status");
        var ownQueue = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue");

        Assert.Equal(1, status.GetProperty("waitingCount").GetInt32());
        Assert.Equal(1, status.GetProperty("estimatedWaitMinutes").GetInt32());
        Assert.Equal(0, ownQueue.GetProperty("estimatedWaitMinutes").GetInt32());
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, true, false)]
    [InlineData(2, false, false)]
    [InlineData(2, true, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, true)]
    public async Task StudentQueueEstimatesAreNullWithoutPositiveCompletedDurations(
        int waitingCount, bool includeZeroDurationCompletion, bool includeActiveRequest)
    {
        var studentId = AuthenticateStudent();
        var session = await new CreateSessionCommand("owner", "Unknown estimate").ExecuteAsync();
        await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
        var queuedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        if (includeZeroDurationCompletion)
            _requests.Saved.Add(CompletedActivity(session.Id, 1, "helped", HelpType.Digital, queuedAt, 0));
        if (includeActiveRequest)
        {
            var active = PendingActivity(session.Id, 1, "active", HelpType.Physical, queuedAt);
            active.Reserve("owner", queuedAt.AddMinutes(1));
            active.Start("owner", queuedAt.AddMinutes(2));
            _requests.Saved.Add(active);
        }
        for (var index = 0; index < waitingCount; index++)
            _requests.Saved.Add(PendingActivity(session.Id, 1,
                index == 0 ? studentId : $"waiting-{index}", HelpType.Digital, queuedAt.AddSeconds(index)));

        var status = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue/status");
        var ownQueue = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue");

        Assert.Equal(waitingCount, status.GetProperty("waitingCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("estimatedWaitMinutes").ValueKind);
        Assert.Equal(JsonValueKind.Null, ownQueue.GetProperty("estimatedWaitMinutes").ValueKind);
        if (waitingCount > 0) Assert.Equal(1, ownQueue.GetProperty("position").GetInt32());
    }

    [Theory]
    [InlineData(HelpType.Physical)]
    [InlineData(HelpType.Digital)]
    public async Task AcceptedFaqWithoutServiceDurationDoesNotCreateAQueueEstimate(HelpType helpType)
    {
        var studentId = AuthenticateStudent();
        var session = await new CreateSessionCommand("owner", "FAQ completion estimate").ExecuteAsync();
        await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
        var queuedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        _requests.Saved.AddRange([
            AcceptedFaqActivity(session.Id, 1, "helped", helpType, queuedAt, 8),
            PendingActivity(session.Id, 1, studentId, helpType, queuedAt.AddMinutes(1))
        ]);

        var status = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue/status");
        var ownQueue = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue");

        Assert.Equal(1, status.GetProperty("waitingCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("estimatedWaitMinutes").ValueKind);
        Assert.Equal(JsonValueKind.Null, ownQueue.GetProperty("estimatedWaitMinutes").ValueKind);
    }

    [Theory]
    [InlineData(HelpType.Physical)]
    [InlineData(HelpType.Digital)]
    public async Task EmptyStudentQueueHasZeroEstimateWhenCompletedDurationIsKnown(HelpType helpType)
    {
        var studentId = AuthenticateStudent();
        var session = await new CreateSessionCommand("owner", "Known empty queue").ExecuteAsync();
        await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
        _requests.Saved.Add(CompletedActivity(session.Id, 1, "helped", helpType,
            DateTimeOffset.UtcNow.AddMinutes(-10), 4));

        var status = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue/status");

        Assert.Equal(0, status.GetProperty("waitingCount").GetInt32());
        Assert.Equal(0, status.GetProperty("estimatedWaitMinutes").GetInt32());
    }

    [Fact]
    public async Task TeacherQueueStatusUsesActualFrontIncludingFaqAndReturnedRequests()
    {
        var session = await new CreateSessionCommand("owner", "Queue priority").ExecuteAsync();
        var now = DateTimeOffset.UtcNow;
        var oldest = PendingActivity(session.Id, 1, "oldest", HelpType.Digital, now.AddMinutes(-40));
        var returned = PendingActivity(session.Id, 1, "returned", HelpType.Physical, now.AddMinutes(-5));
        returned.Reserve("owner", now.AddMinutes(-4));
        returned.Start("owner", now.AddMinutes(-3));
        returned.Cancel("owner", AuthorRole.Teacher, now.AddMinutes(-1));
        returned.Reserve("owner", now);
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), session.Id, 1, new MessageText("FAQ question"), now.AddMinutes(-2));
        var active = PendingActivity(session.Id, 1, "active", HelpType.Physical, now.AddMinutes(-50));
        active.Reserve("owner", now.AddMinutes(-45));
        active.Start("owner", now.AddMinutes(-45));
        _requests.Saved.AddRange([oldest, returned, faq, active]);
        Authenticate("Teacher", "owner");

        var before = DateTimeOffset.UtcNow;
        var status = await ReadActivityAsync($"/api/teachers/sessions/{session.Id}/queue/status");
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(3, status.GetProperty("waitingCount").GetInt32());
        Assert.Equal(2, status.GetProperty("unreservedWaitingCount").GetInt32());
        Assert.Equal(active.Id, status.GetProperty("myReservedRequestId").GetGuid());
        Assert.Equal(1, status.GetProperty("generation").GetInt32());
        AssertFrontWait(status, faq.QueuedAt, before, after);
        AssertProperties(status, "waitingCount", "unreservedWaitingCount", "frontWaitMinutes", "generation", "myReservedRequestId");

        var fullQueue = await ReadActivityAsync($"/api/teachers/sessions/{session.Id}/queue");
        Assert.Equal(active.Id, fullQueue.GetProperty("myReservedRequestId").GetGuid());

        _requests.Saved.Remove(faq);
        before = DateTimeOffset.UtcNow;
        var withoutFaq = await ReadActivityAsync($"/api/teachers/sessions/{session.Id}/queue/status");
        after = DateTimeOffset.UtcNow;

        Assert.Equal(2, withoutFaq.GetProperty("waitingCount").GetInt32());
        AssertFrontWait(withoutFaq, returned.QueuedAt, before, after);
    }

    [Theory]
    [InlineData(HelpType.Physical, false)]
    [InlineData(HelpType.Digital, false)]
    [InlineData(HelpType.Physical, true)]
    public async Task TeacherQueueResultsExposeOwnPendingOrActiveRequestAndAvailableCount(HelpType helpType, bool startHelp)
    {
        var session = await new CreateSessionCommand("owner", "Own assignment").ExecuteAsync();
        var now = DateTimeOffset.UtcNow;
        var own = PendingActivity(session.Id, 1, "own-student", helpType, now.AddMinutes(-10));
        own.Reserve("owner", now.AddMinutes(-5));
        if (startHelp) own.Start("owner", now.AddMinutes(-4));
        var other = PendingActivity(session.Id, 1, "other-student", HelpType.Digital, now.AddMinutes(-20));
        other.Reserve("colleague", now.AddMinutes(-15));
        var available = PendingActivity(session.Id, 1, "available-student", HelpType.Physical, now.AddMinutes(-2));
        var faq = HelpRequest.CreateFaq(Guid.NewGuid(), session.Id, 1, new MessageText("FAQ question"), now.AddMinutes(-1));
        _requests.Saved.AddRange([own, other, available, faq]);
        Authenticate("Teacher", "owner");

        foreach (var path in new[] { "queue", "queue/status" })
        {
            var result = await ReadActivityAsync($"/api/teachers/sessions/{session.Id}/{path}");
            Assert.Equal(startHelp ? 3 : 4, result.GetProperty("waitingCount").GetInt32());
            Assert.Equal(2, result.GetProperty("unreservedWaitingCount").GetInt32());
            Assert.Equal(own.Id, result.GetProperty("myReservedRequestId").GetGuid());
        }

        await new JoinTeacherSessionCommand(session.TeacherCode, "colleague").ExecuteAsync();
        _client.DefaultRequestHeaders.Remove("X-Test-Subject");
        _client.DefaultRequestHeaders.Add("X-Test-Subject", "colleague");
        foreach (var path in new[] { "queue", "queue/status" })
        {
            var result = await ReadActivityAsync($"/api/teachers/sessions/{session.Id}/{path}");
            Assert.Equal(other.Id, result.GetProperty("myReservedRequestId").GetGuid());
        }
    }

    [Fact]
    public async Task TeacherQueueResultsIgnoreClosedOtherSessionAndPreviousGenerationAssignments()
    {
        var session = await new CreateSessionCommand("owner", "Current assignment").ExecuteAsync();
        var storedSession = (await _app.Services.GetRequiredService<ISessionRepository>().GetAsync(session.Id, default))!;
        storedSession.Clear(1, DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var stale = PendingActivity(session.Id, 1, "stale-student", HelpType.Digital, now.AddMinutes(-30));
        stale.Reserve("owner", now.AddMinutes(-25));
        var unrelated = PendingActivity(Guid.NewGuid(), 2, "unrelated-student", HelpType.Physical, now.AddMinutes(-20));
        unrelated.Reserve("owner", now.AddMinutes(-15));
        unrelated.Start("owner", now.AddMinutes(-14));
        var cancelled = PendingActivity(session.Id, 2, "cancelled-student", HelpType.Digital, now.AddMinutes(-10));
        cancelled.Reserve("owner", now.AddMinutes(-9));
        cancelled.Cancel("cancelled-student", AuthorRole.Student, now.AddMinutes(-8));
        var other = PendingActivity(session.Id, 2, "other-student", HelpType.Digital, now.AddMinutes(-5));
        other.Reserve("colleague", now.AddMinutes(-4));
        _requests.Saved.AddRange([
            stale, unrelated, cancelled, other,
            CompletedActivity(session.Id, 2, "completed-student", HelpType.Physical, now.AddMinutes(-20), 5),
            PendingActivity(session.Id, 2, "available-student", HelpType.Digital, now.AddMinutes(-1))
        ]);
        Authenticate("Teacher", "owner");

        foreach (var path in new[] { "queue", "queue/status" })
        {
            var result = await ReadActivityAsync($"/api/teachers/sessions/{session.Id}/{path}");
            Assert.Equal(2, result.GetProperty("generation").GetInt32());
            Assert.Equal(2, result.GetProperty("waitingCount").GetInt32());
            Assert.Equal(1, result.GetProperty("unreservedWaitingCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("myReservedRequestId").ValueKind);
        }
    }

    [Fact]
    public async Task QueueStatusesUseCurrentGenerationAfterClear()
    {
        var studentId = AuthenticateStudent();
        Authenticate("Teacher", "owner");
        var session = await new CreateSessionCommand("owner", "New generation").ExecuteAsync();
        await new JoinSessionCommand(session.Code, studentId).ExecuteAsync();
        var queuedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        _requests.Saved.AddRange([
            PendingActivity(session.Id, 1, studentId, HelpType.Digital, queuedAt),
            CompletedActivity(session.Id, 1, "helped", HelpType.Digital, queuedAt, 2)
        ]);
        var storedSession = (await _app.Services.GetRequiredService<ISessionRepository>().GetAsync(session.Id, default))!;
        storedSession.Clear(1, DateTimeOffset.UtcNow);

        var teacherStatus = await ReadActivityAsync($"/api/teachers/sessions/{session.Id}/queue/status");
        var studentStatus = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue/status");

        Assert.Equal(0, teacherStatus.GetProperty("waitingCount").GetInt32());
        Assert.Equal(0, teacherStatus.GetProperty("unreservedWaitingCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, teacherStatus.GetProperty("myReservedRequestId").ValueKind);
        Assert.Equal(2, teacherStatus.GetProperty("generation").GetInt32());
        Assert.Equal(JsonValueKind.Null, teacherStatus.GetProperty("frontWaitMinutes").ValueKind);
        Assert.Equal(0, studentStatus.GetProperty("waitingCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, studentStatus.GetProperty("estimatedWaitMinutes").ValueKind);

        _requests.Saved.Add(PendingActivity(session.Id, 2, "current-student", HelpType.Physical, queuedAt));
        teacherStatus = await ReadActivityAsync($"/api/teachers/sessions/{session.Id}/queue/status");
        studentStatus = await ReadActivityAsync($"/api/students/sessions/{session.Id}/queue/status");
        Assert.Equal(1, teacherStatus.GetProperty("waitingCount").GetInt32());
        Assert.Equal(1, studentStatus.GetProperty("waitingCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, studentStatus.GetProperty("estimatedWaitMinutes").ValueKind);
    }

    [Theory]
    [InlineData("teachers", "statistics")]
    [InlineData("teachers", "queue/status")]
    [InlineData("students", "queue/status")]
    public async Task SessionActivityEndpointsHideSessionsFromUnrelatedUsers(string role, string resource)
    {
        var session = await new CreateSessionCommand("owner", "Private").ExecuteAsync();
        if (role == "teachers") Authenticate("Teacher", "unrelated-teacher");
        else AuthenticateStudent();

        using var response = await _client.GetAsync($"/api/{role}/sessions/{session.Id}/{resource}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("statistics")]
    [InlineData("queue/status")]
    public async Task JoinedTeacherCanReadSessionStatisticsAndQueueStatus(string resource)
    {
        var session = await new CreateSessionCommand("owner", "Shared activity").ExecuteAsync();
        await new JoinTeacherSessionCommand(session.TeacherCode, "colleague").ExecuteAsync();
        Authenticate("Teacher", "colleague");

        using var response = await _client.GetAsync($"/api/teachers/sessions/{session.Id}/{resource}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("teachers", "statistics")]
    [InlineData("teachers", "queue/status")]
    [InlineData("students", "queue/status")]
    public async Task SessionActivityEndpointsRequireAuthentication(string role, string resource)
    {
        using var response = await _client.GetAsync($"/api/{role}/sessions/{Guid.NewGuid()}/{resource}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<JsonElement> ReadActivityAsync(string path)
    {
        using var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static HelpRequest PendingActivity(Guid sessionId, int generation, string studentId, HelpType helpType, DateTimeOffset queuedAt) =>
        HelpRequest.Create(sessionId, generation, studentId, helpType, new MessageText("Question"), queuedAt);

    private static HelpRequest CompletedActivity(Guid sessionId, int generation, string studentId, HelpType helpType,
        DateTimeOffset queuedAt, double waitMinutes)
    {
        var request = PendingActivity(sessionId, generation, studentId, helpType, queuedAt);
        request.Reserve("owner", queuedAt);
        var closedAt = queuedAt.AddMinutes(waitMinutes);
        if (helpType == HelpType.Physical)
        {
            request.Start("owner", queuedAt.AddMinutes(1));
            request.Complete("owner", closedAt);
        }
        else request.Answer("owner", new MessageText("Answer"), closedAt);
        return request;
    }

    private static HelpRequest CompletedFaqActivity(Guid sessionId, int generation, DateTimeOffset queuedAt, int waitMinutes)
    {
        var request = HelpRequest.CreateFaq(Guid.NewGuid(), sessionId, generation, new MessageText("FAQ question"), queuedAt);
        request.Reserve("owner", queuedAt);
        request.Answer("owner", new MessageText("FAQ answer"), queuedAt.AddMinutes(waitMinutes));
        return request;
    }

    private static HelpRequest AcceptedFaqActivity(Guid sessionId, int generation, string studentId, HelpType helpType,
        DateTimeOffset queuedAt, int waitMinutes)
    {
        var request = PendingActivity(sessionId, generation, studentId, helpType, queuedAt);
        var faqId = Guid.NewGuid();
        request.LinkToFaq(faqId, new MessageText("Shared question"), queuedAt);
        request.SuggestFaq(faqId, "owner", new MessageText("FAQ answer"), queuedAt.AddMinutes(waitMinutes - 1));
        request.AcceptFaq(studentId, queuedAt.AddMinutes(waitMinutes));
        return request;
    }

    private static void AssertProperties(JsonElement result, params string[] expected) =>
        Assert.Equal(expected.Order(), result.EnumerateObject().Select(property => property.Name).Order());

    private static void AssertFrontWait(JsonElement result, DateTimeOffset queuedAt, DateTimeOffset before, DateTimeOffset after) =>
        Assert.InRange(result.GetProperty("frontWaitMinutes").GetDouble(),
            (before - queuedAt).TotalMinutes - 0.000001, (after - queuedAt).TotalMinutes + 0.000001);
}
