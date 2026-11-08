using System.Net;
using System.Net.Http.Json;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task MoreWritersThanPoolConnectionsLeaveCapacityForDomainEventReaders()
    {
        var sessions = new[]
        {
            await CreateAsync("Contended queue 1"),
            await CreateAsync("Contended queue 2"),
            await CreateAsync("Contended queue 3")
        };
        var clients = new List<HttpClient>();
        try
        {
            // Reproduce the observed three-queue contention with more writers than
            // the shared pool's forty connections; holders need cross-module readers.
            for (var number = 0; number < 64; number++)
                clients.Add(await OtherStudentAsync(sessions[number % sessions.Length].Code));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var responses = await Task.WhenAll(clients.Select((client, number) => client.PostAsJsonAsync(
                $"/api/students/sessions/{sessions[number % sessions.Length].Id}/requests",
                new { Message = "Concurrent question", HelpType = "Digital" }, deadline.Token)));

            try { Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode)); }
            finally { foreach (var response in responses) response.Dispose(); }
            var notifications = await ReadNotificationsAsync(null);
            var queueStarts = notifications.Where(item => item.Message.EventType == NotificationEventType.HelpRequestCreated).ToArray();
            Assert.Equal(sessions.Length, queueStarts.Length);
            Assert.Equal(sessions.Select(session => (Guid?)session.Id).Order(),
                queueStarts.Select(item => item.Message.SessionId).Order());
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }
}
