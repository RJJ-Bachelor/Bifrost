using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.IntegrationTests.Infrastructure;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimirService.Infrastructure.Persistence;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Testing;

namespace Backend.IntegrationTests.Scenarios;

[Collection("Backend hosts")]
[Trait("Category", "ExternalIntegration")]
public sealed class ReportScenarioTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [ReportScenarioFact]
    public async Task IT01_HelpRequestThroughGatewayReachesQueueAndAnswerArrivesThroughGnaSignalR()
    {
        await using var fixture = await ReportScenarioFixture.CreateAsync();
        using var teacher = fixture.Teacher();
        using var student = fixture.Student();
        var session = await CreateSessionAsync(teacher, "IT-01 session", fixture.Token);
        await JoinAsync(student, session.Code, fixture.Token);
        using var socket = await fixture.ConnectStudentAsync(student, fixture.Token);

        var request = await CreateRequestAsync(student, session.Id, "IT-01 private question", fixture.Token);

        using (var queue = await teacher.GetAsync($"/api/teachers/sessions/{session.Id}/queue", fixture.Token))
        {
            var body = await ReadAsync<JsonElement>(queue, HttpStatusCode.OK, fixture.Token);
            Assert.Equal(1, body.GetProperty("waitingCount").GetInt32());
            Assert.Equal(request.Id,
                Assert.Single(body.GetProperty("requests").EnumerateArray()).GetProperty("id").GetGuid());
        }

        using (var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null, fixture.Token))
            Assert.Equal(request.Id, (await ReadAsync<HelpRequestResult>(take, HttpStatusCode.OK, fixture.Token)).Id);

        const string answerText = "IT-01 private answer";
        using (var response = await teacher.PostAsJsonAsync($"/api/teachers/requests/{request.Id}/messages",
                   new { Text = answerText }, fixture.Token))
        {
            var answered = await ReadAsync<HelpRequestResult>(response, HttpStatusCode.OK, fixture.Token);
            Assert.Equal((request.Id, session.Id, session.CurrentGeneration, HelpRequestStatus.CompletedByDigital),
                (answered.Id, answered.SessionId, answered.Generation, answered.Status));
        }

        var notification = await ReadNotificationAsync(socket,
            message => message.EventType == NotificationEventType.HelpRequestAnswered && message.HelpRequestId == request.Id,
            fixture.Token);
        Assert.Equal((request.StudentId, session.Id, session.CurrentGeneration, "Answered", "CompletedByDigital"),
            (notification.UserId, notification.SessionId, notification.Generation, notification.Change, notification.Status));

        using (var response = await student.GetAsync($"/api/students/requests/{request.Id}", fixture.Token))
        {
            var saved = await ReadAsync<HelpRequestResult>(response, HttpStatusCode.OK, fixture.Token);
            Assert.Equal(HelpRequestStatus.CompletedByDigital, saved.Status);
            Assert.Equal(answerText, Assert.Single(saved.Messages, message => message.MessageType == HelpRequestMessageType.TeacherAnswer).Text);
        }

        using (var queue = await teacher.GetAsync($"/api/teachers/sessions/{session.Id}/queue", fixture.Token))
        {
            var body = await ReadAsync<JsonElement>(queue, HttpStatusCode.OK, fixture.Token);
            Assert.Equal(0, body.GetProperty("waitingCount").GetInt32());
            Assert.Empty(body.GetProperty("requests").EnumerateArray());
        }
    }

    [ReportScenarioFact]
    public async Task IT03_GatewayAndRealEirRejectInvalidCredentialsRolesAndSessionAccessWithoutLeakingData()
    {
        await using var fixture = await ReportScenarioFixture.CreateAsync();
        using var owner = fixture.Teacher();
        using var student = fixture.Student();
        var privateTitle = "IT-03 private title " + Guid.NewGuid().ToString("N");
        var privateQuestion = "IT-03 private question " + Guid.NewGuid().ToString("N");
        var session = await CreateSessionAsync(owner, privateTitle, fixture.Token);
        await JoinAsync(student, session.Code, fixture.Token);
        var request = await CreateRequestAsync(student, session.Id, privateQuestion, fixture.Token);
        var paths = new[] { $"/api/teachers/sessions/{session.Id}", $"/api/teachers/requests/{request.Id}" };

        foreach (var path in paths)
        {
            using var allowed = await owner.GetAsync(path, fixture.Token);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Contains(path.Contains("/requests/", StringComparison.Ordinal) ? privateQuestion : privateTitle,
                await allowed.Content.ReadAsStringAsync(fixture.Token));
        }

        var cases = new (Func<HttpClient> Client, HttpStatusCode Status)[]
        {
            (() => fixture.Teacher(tokenOverride: "invalid-token"), HttpStatusCode.Unauthorized),
            (() => fixture.Teacher(tokenOverride: fixture.TeacherToken(expires: DateTime.UtcNow.AddMinutes(-10))), HttpStatusCode.Unauthorized),
            (() => fixture.Teacher(role: "Student"), HttpStatusCode.Forbidden),
            (() => fixture.Teacher(scope: "Other"), HttpStatusCode.Forbidden),
            (() => fixture.Teacher(subject: "nonmember"), HttpStatusCode.NotFound)
        };
        foreach (var (createClient, status) in cases)
        {
            using var client = createClient();
            foreach (var path in paths)
            {
                using var response = await client.GetAsync(path, fixture.Token);
                var body = await response.Content.ReadAsStringAsync(fixture.Token);
                Assert.True(response.StatusCode == status, $"Expected {status} for {path}, got {response.StatusCode}: {body}");
                Assert.DoesNotContain(privateTitle, body, StringComparison.Ordinal);
                Assert.DoesNotContain(privateQuestion, body, StringComparison.Ordinal);
                Assert.DoesNotContain(session.Code, body, StringComparison.Ordinal);
                Assert.DoesNotContain(session.TeacherCode, body, StringComparison.Ordinal);
            }
        }
    }

    [ReportScenarioFact]
    public async Task IT04_RequestAndOutboxSurviveBrokerOutageAndReachMimirAfterReconnect()
    {
        await using var fixture = await ReportScenarioFixture.CreateAsync();
        using var teacher = fixture.Teacher();
        using var student = fixture.Student();
        var session = await CreateSessionAsync(teacher, "IT-04 session", fixture.Token);
        await WarmUpDeliveryAsync(fixture, session);
        await JoinAsync(student, session.Code, fixture.Token);
        await fixture.DisconnectBrokerAsync();

        var request = await CreateRequestAsync(student, session.Id, "IT-04 request during broker outage", fixture.Token);
        var pending = await WaitForFailedCreationAsync(fixture, request);

        fixture.ResumeBroker();

        await AssertCreationDeliveredAsync(fixture, request, pending.MessageId);
    }

    [ReportScenarioFact]
    public async Task IT06_PendingOutboxIsDeliveredAfterBackendRestartWithoutResubmittingTheRequest()
    {
        await using var fixture = await ReportScenarioFixture.CreateAsync();
        using var teacher = fixture.Teacher();
        using var student = fixture.Student();
        var session = await CreateSessionAsync(teacher, "IT-06 session", fixture.Token);
        await WarmUpDeliveryAsync(fixture, session);
        await JoinAsync(student, session.Code, fixture.Token);
        await fixture.DisconnectBrokerAsync();

        var request = await CreateRequestAsync(student, session.Id, "IT-06 durable request before restart", fixture.Token);
        var pending = await WaitForFailedCreationAsync(fixture, request);

        await fixture.RestartBackendAsync();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            Assert.True(await db.Requests.AnyAsync(item => item.Id == request.Id, fixture.Token));
            Assert.Equal(pending.MessageId, (await db.Outbox.AsNoTracking().SingleAsync(item => item.MessageId == pending.MessageId, fixture.Token)).MessageId);
        }
        fixture.ResumeBroker();

        await AssertCreationDeliveredAsync(fixture, request, pending.MessageId);
        using var response = await student.GetAsync($"/api/students/requests/{request.Id}", fixture.Token);
        var saved = await ReadAsync<HelpRequestResult>(response, HttpStatusCode.OK, fixture.Token);
        Assert.Equal(request.Id, saved.Id);
        Assert.Equal(request.Messages.Single().Text, Assert.Single(saved.Messages).Text);
    }

    private static async Task WarmUpDeliveryAsync(ReportScenarioFixture fixture, SessionResult session)
    {
        using var student = fixture.Student();
        await JoinAsync(student, session.Code, fixture.Token);
        var warmup = await CreateRequestAsync(student, session.Id, "Warm-up verifies the RabbitMQ consumer is ready", fixture.Token);
        await ReportScenarioFixture.WaitUntilAsync(async () =>
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<MimirRequestDbContext>().Requests
                .AnyAsync(item => item.Id == warmup.Id, fixture.Token);
        }, fixture.Token);
    }

    private static async Task<OutboxMessage> WaitForFailedCreationAsync(ReportScenarioFixture fixture, HelpRequestResult request)
    {
        await ReportScenarioFixture.WaitUntilAsync(async () =>
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var row = await CreationOutboxAsync(scope.ServiceProvider.GetRequiredService<EirRequestDbContext>(), request.Id, fixture.Token);
            return row.FailureCount > 0 && row.PublishedAtUtc is null;
        }, fixture.Token);

        await using var verification = fixture.Services.CreateAsyncScope();
        var eir = verification.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var saved = await eir.Requests.AsNoTracking().Include(item => item.Messages).SingleAsync(item => item.Id == request.Id, fixture.Token);
        Assert.Equal((request.SessionId, request.Generation, HelpRequestStatus.Pending), (saved.SessionId, saved.Generation, saved.Status));
        Assert.Equal(request.Messages.Single().Text, Assert.Single(saved.Messages).Text.Value);
        var pending = await CreationOutboxAsync(eir, request.Id, fixture.Token);
        Assert.Null(pending.PublishedAtUtc);
        Assert.NotNull(pending.LastAttemptAtUtc);
        Assert.NotNull(pending.LastError);
        Assert.False(await verification.ServiceProvider.GetRequiredService<MimirRequestDbContext>().Requests.AnyAsync(item => item.Id == request.Id, fixture.Token));
        return pending;
    }

    private static async Task AssertCreationDeliveredAsync(ReportScenarioFixture fixture, HelpRequestResult request, Guid messageId)
    {
        await ReportScenarioFixture.WaitUntilAsync(async () =>
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var eir = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            var mimir = scope.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
            return await eir.Outbox.AnyAsync(item => item.MessageId == messageId && item.PublishedAtUtc != null, fixture.Token)
                && await mimir.Requests.AnyAsync(item => item.Id == request.Id, fixture.Token);
        }, fixture.Token);

        await using var verification = fixture.Services.CreateAsyncScope();
        var received = await verification.ServiceProvider.GetRequiredService<MimirRequestDbContext>().Requests.AsNoTracking()
            .SingleAsync(item => item.Id == request.Id, fixture.Token);
        Assert.Equal((request.SessionId, request.Generation, request.Messages.Single().Text),
            (received.SessionId, received.Generation, received.Message));
        var row = await CreationOutboxAsync(verification.ServiceProvider.GetRequiredService<EirRequestDbContext>(), request.Id, fixture.Token);
        Assert.Equal(messageId, row.MessageId);
        Assert.NotNull(row.PublishedAtUtc);
    }

    private static async Task<OutboxMessage> CreationOutboxAsync(EirRequestDbContext db, Guid requestId, CancellationToken token)
    {
        var rows = await db.Outbox.AsNoTracking().Where(item => item.RoutingKey == MessageRoutes.HelpRequestCreated).ToArrayAsync(token);
        return Assert.Single(rows, row => JsonSerializer.Deserialize<HelpRequestCreatedMessage>(row.Payload, JsonOptions)!.HelpRequestId == requestId);
    }

    private static async Task<SessionResult> CreateSessionAsync(HttpClient teacher, string title, CancellationToken token)
    {
        using var response = await teacher.PostAsJsonAsync("/api/teachers/sessions", new { Title = title }, token);
        return await ReadAsync<SessionResult>(response, HttpStatusCode.Created, token);
    }

    private static async Task JoinAsync(HttpClient student, string code, CancellationToken token)
    {
        using var response = await student.PostAsJsonAsync("/api/students/sessions/join", new { Code = code }, token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(token));
    }

    private static async Task<HelpRequestResult> CreateRequestAsync(HttpClient student, Guid sessionId, string question, CancellationToken token)
    {
        using var response = await student.PostAsJsonAsync($"/api/students/sessions/{sessionId}/requests",
            new { Message = question, HelpType = "Digital" }, token);
        return await ReadAsync<HelpRequestResult>(response, HttpStatusCode.Created, token);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected, CancellationToken token)
    {
        var body = await response.Content.ReadAsStringAsync(token);
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private static async Task<NotificationDeliveryRequestedMessage> ReadNotificationAsync(WebSocket socket,
        Func<NotificationDeliveryRequestedMessage, bool> matches, CancellationToken token)
    {
        while (true)
        {
            using var frame = JsonDocument.Parse((await SignalRTestClient.ReadAsync(socket, token)).TrimEnd('\u001e'));
            Assert.Equal("NotificationReceived", frame.RootElement.GetProperty("target").GetString());
            var message = frame.RootElement.GetProperty("arguments")[0].Deserialize<NotificationDeliveryRequestedMessage>(JsonOptions)!;
            if (matches(message)) return message;
        }
    }
}
