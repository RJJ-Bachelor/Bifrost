using EirService.Contracts.HelpRequests.HandleSessionChange;
using EirService.Contracts.Messaging;
using Shared.Application.Messaging.Outbox;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.Contracts.Participants.GetSessionParticipantIds;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task LifecycleChangesNotifyEveryParticipantAndCreatorOnceWithoutPushOrPrivateRequestData()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var otherStudent = await OtherStudentAsync(session.Code);
        using var colleague = await JoinTeacherAsync(session.TeacherCode);
        var unrelated = await CreateAsync("Unrelated session");
        using var outsider = await OtherStudentAsync(unrelated.Code);
        var studentId = (await student.GetFromJsonAsync<JsonElement>("/api/students/me")).GetProperty("userId").GetString()!;
        var otherStudentId = (await otherStudent.GetFromJsonAsync<JsonElement>("/api/students/me")).GetProperty("userId").GetString()!;
        string[] recipients = [session.CreatedByTeacherId, "colleague", studentId, otherStudentId];

        // The owner receives lifecycle changes even without an explicit membership.
        using var cleared = await ClearAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        await AssertLifecycleNotificationsAsync(session.Id, "Cleared", 2, recipients);

        // Membership in multiple roles and an explicitly joined owner must not duplicate delivery.
        using var ownerJoin = await teacher.PostAsJsonAsync("/api/teachers/sessions/join", new { Code = session.TeacherCode });
        Assert.Equal(HttpStatusCode.OK, ownerJoin.StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IParticipantRepository>().JoinAsync(
                SessionParticipant.Join(session.Id, "colleague", ParticipantRole.Student, clock.Now), CancellationToken.None);
            var query = scope.ServiceProvider.GetRequiredService<ICommandHandler<GetSessionParticipantIdsQuery, string[]>>();
            var participants = await query.ExecuteAsync(new GetSessionParticipantIdsQuery(session.Id), CancellationToken.None);
            Assert.Equal(recipients.Order(), participants.Order());
        }
        using var archived = await ArchiveAsync(session.Id, 2);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        await AssertLifecycleNotificationsAsync(session.Id, "Archived", 3, recipients);

        // Deletion still resolves recipients and the creator before the session is removed.
        using var deleted = await teacher.DeleteAsync($"/api/teachers/sessions/{session.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await AssertLifecycleNotificationsAsync(session.Id, "Deleted", 3, recipients);
        using var staleArchive = await ArchiveAsync(session.Id, 3);
        Assert.Equal(HttpStatusCode.NotFound, staleArchive.StatusCode);
        await AssertLifecycleNotificationsAsync(session.Id, "Deleted", 3, recipients);
    }

    [PostgresFact]
    public async Task RejectedLifecycleNotificationRollsBackTheGenerationAndAllStagedMessagesAndAllowsRetry()
    {
        var session = await CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE sessions.outbox ADD CONSTRAINT "RejectLifecycleNotificationForTest"
            CHECK ("RoutingKey" <> 'session.changed');
            """);
        var aggregate = (await scope.ServiceProvider.GetRequiredService<ISessionRepository>().GetAsync(session.Id, CancellationToken.None))!;
        aggregate.Clear(1, clock.Now);
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<ISessionRepository>>();
        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync());
        await using (var verification = app.Services.CreateAsyncScope())
        {
            var saved = verification.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            Assert.Equal(1, (await saved.Sessions.SingleAsync(candidate => candidate.Id == session.Id)).CurrentGeneration);
            Assert.Empty(await saved.Outbox.ToArrayAsync());
            await saved.Database.ExecuteSqlRawAsync("""
                ALTER TABLE sessions.outbox DROP CONSTRAINT "RejectLifecycleNotificationForTest";
                """);
        }
        await unitOfWork.SaveChangesAsync();
        await AssertLifecycleNotificationsAsync(session.Id, "Cleared", 2, [session.CreatedByTeacherId]);
    }

    private async Task AssertLifecycleNotificationsAsync(Guid sessionId, string change, int generation, string[] recipients)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var messages = (await db.Outbox.AsNoTracking()
            .Where(message => message.RoutingKey == MessageRoutes.SessionChanged).ToArrayAsync())
            .Select(message => (Row: message, Payload: JsonSerializer.Deserialize<SessionChangedMessage>(message.Payload, options)!))
            .Where(message => message.Payload.SessionId == sessionId && message.Payload.Change == change).ToArray();
        var broadcast = Assert.Single(messages);
        Assert.Equal(MessageRoutes.Notifications, broadcast.Row.ExchangeName);
        Assert.Equal(generation, broadcast.Payload.Generation);
        Assert.Equal(recipients.Order(), broadcast.Payload.UserIds.Order());
        Assert.Equal(recipients.Length, broadcast.Payload.UserIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(await db.Outbox.Where(message => message.RoutingKey == MessageRoutes.NotificationCreated).ToArrayAsync());
        using var payload = JsonDocument.Parse(broadcast.Row.Payload);
        Assert.Equal(new[] { "change", "generation", "sessionId", "userIds" },
            payload.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.All(SessionNotificationTestProjection.Expand(broadcast.Row.MessageId, broadcast.Payload), message =>
        {
            Assert.Equal(NotificationEventType.SessionChanged, message.Message.EventType);
            Assert.Equal(NotificationDeliveryType.RealtimeOnly, message.Message.DeliveryType);
            Assert.Null(message.Message.HelpRequestId);
            Assert.Null(message.Message.Status);
        });
        var integrationEvents = (await db.Outbox.AsNoTracking()
            .Where(message => message.RoutingKey == MessageRoutes.SessionUpdated).ToArrayAsync())
            .Select(message => JsonSerializer.Deserialize<SessionUpdatedIntegrationEvent>(message.Payload, options)!)
            .Where(message => message.SessionId == sessionId && message.EventType.ToString() == change).ToArray();
        var integrationEvent = Assert.Single(integrationEvents);
        Assert.Equal(generation, integrationEvent.Generation);
        var internalCommands = (await db.Outbox.AsNoTracking()
            .Where(message => message.ExchangeName == InternalMessageRoutes.Exchange &&
                message.RoutingKey == EirModuleRoutes.SessionChanged).ToArrayAsync())
            .Select(message => JsonSerializer.Deserialize<HandleSessionChangeCommand>(message.Payload, options)!)
            .Where(command => command.EventId == integrationEvent.EventId).ToArray();
        Assert.Equal(new HandleSessionChangeCommand(integrationEvent.EventId, sessionId, generation,
            Enum.Parse<SessionChangeType>(change), integrationEvent.UpdatedAtUtc, integrationEvent.PreviousGeneration),
            Assert.Single(internalCommands));
    }
}
