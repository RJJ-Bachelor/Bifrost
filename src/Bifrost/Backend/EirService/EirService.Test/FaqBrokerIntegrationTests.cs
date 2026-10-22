using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RabbitMQ.Client;
using EirService.Contracts.HelpRequests.ApplyGeneralization;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.KnowledgeBase.Application.Features.Internal.FaqAnswered;
using EirService.KnowledgeBase.Application.Features.Internal.RegisterGeneralization;
using FastEndpoints;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MimirService.Application.Services.Messaging;
using MimirService.Infrastructure;
using MimirService.Infrastructure.Persistence;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Infrastructure.Persistence.Outbox;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Application.Features.Common;
using Shared.Domain.Notifications;
using System.Collections.Concurrent;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [OutboxBrokerFact]
    public async Task DummyGeneralizationTravelsFromMimirOutboxThroughRabbitAndPersistsTheTeachersAnswer()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        using var second = await OtherStudentAsync(session.Code);
        var firstId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var secondId = await SubmitAsync(second, session.Id, HelpType.Digital);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:mimir"] = connectionString,
            ["ConnectionStrings:rabbitmq"] = Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMimirInfrastructure(configuration);
        await using var mimir = services.BuildServiceProvider();
        var messageBus = mimir.GetRequiredService<IMessageBus>();
        var prefix = "faq-test-" + Guid.NewGuid().ToString("N");
        try
        {
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var studentsNotified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var notices = new ConcurrentDictionary<Guid, NotificationCreatedMessage>();
            var deliveries = 0;
            await using var generalized = await messageBus.SubscribeAsync<GeneralizedCreatedMessage>(prefix + "-created",
                MessageRoutes.Requests, MessageRoutes.GeneralizedCreated, async (message, _) =>
                {
                    if (Interlocked.Increment(ref deliveries) == 1)
                    {
                        await using var scope = app.Services.CreateAsyncScope();
                        var handler = ActivatorUtilities.CreateInstance<RegisterGeneralizationMessageHandler>(
                            scope.ServiceProvider, new FailingApplyGeneralizationHandler());
                        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(message, CancellationToken.None));
                        await using var verification = app.Services.CreateAsyncScope();
                        var faq = await verification.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>()
                            .Faqs.SingleAsync();
                        Assert.Equal(message.Payload.GeneralizationId, faq.Id);
                        Assert.False(await verification.ServiceProvider.GetRequiredService<EirRequestDbContext>()
                            .Requests.AnyAsync(request => request.Id == message.Payload.GeneralizationId));
                        throw new IOException("Transient failure after persisting the canonical FAQ.");
                    }
                    await DeliverGeneralizationAsync(message.Payload);
                    received.TrySetResult();
                });
            await using var answerSubscription = await messageBus.SubscribeAsync<FaqAnsweredMessage>(prefix + "-answered",
                MessageRoutes.Requests, MessageRoutes.FaqAnswered, async (message, ct) =>
                {
                    await using var scope = app.Services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<FaqAnsweredMessageHandler>().HandleAsync(message.Payload, ct);
                    answered.TrySetResult();
                });
            await using var updates = await messageBus.SubscribeAsync<JsonElement>(prefix + "-updates", MessageRoutes.Requests,
                MessageRoutes.HelpRequestUpdated, (_, _) => Task.CompletedTask);
            await using var originals = await messageBus.SubscribeAsync<JsonElement>(prefix + "-originals", MessageRoutes.Requests,
                MessageRoutes.HelpRequestCreated, (_, _) => Task.CompletedTask);
            await using var notifications = await messageBus.SubscribeAsync<NotificationCreatedMessage>(prefix + "-notifications", MessageRoutes.Notifications,
                MessageRoutes.NotificationCreated, (message, _) =>
                {
                    notices.TryAdd(message.Id, message.Payload);
                    if (notices.Values.Count(notice => notice.EventType == NotificationEventType.FaqAnswered) == 2 &&
                        notices.Values.Any(notice => notice.EventType == NotificationEventType.FaqCreated))
                        studentsNotified.TrySetResult();
                    return Task.CompletedTask;
                });
            var payload = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, 1, "Shared question", [firstId, secondId]);
            await using (var scope = mimir.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
                await db.Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<IGeneralizedMessagePublisher>().PublishAsync(payload, CancellationToken.None);
            }
            var sender = ActivatorUtilities.CreateInstance<OutboxDispatcher<MimirRequestDbContext>>(mimir);
            await sender.PublishPendingAsync(CancellationToken.None);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(2, deliveries);
            foreach (var (client, id) in new[] { (student, firstId), (second, secondId) })
            {
                var source = (await client.GetFromJsonAsync<HelpRequestResult>(
                    $"/api/students/requests/{id}", FaqJsonOptions))!;
                Assert.Equal(HelpRequestStatus.AwaitingFaq, source.Status);
                Assert.Equal(payload.GeneralizationId, source.FaqAnswerId);
                var question = Assert.Single(source.Messages, message => message.AuthorRole == AuthorRole.System);
                Assert.Equal(payload.Question, question.Text);
                Assert.Equal("mimir", question.AuthorId);
            }
            using var take = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
            Assert.Equal(payload.GeneralizationId, (await take.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            using var answer = await teacher.PostAsJsonAsync($"/api/teachers/requests/{payload.GeneralizationId}/messages", new { Text = "Shared answer" });
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            var eirSender = new OutboxDispatcher<EirRequestDbContext>(app.Services.GetRequiredService<IServiceScopeFactory>(),
                messageBus, clock, NullLogger<OutboxDispatcher<EirRequestDbContext>>.Instance);
            await eirSender.PublishPendingAsync(CancellationToken.None);
            await answered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await studentsNotified.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var teacherNotice = Assert.Single(notices.Values, notice => notice.EventType == NotificationEventType.FaqCreated);
            Assert.Equal("provider|opaque-teacher", teacherNotice.UserId);
            Assert.Equal(payload.GeneralizationId, teacherNotice.HelpRequestId);
            var studentNotices = notices.Values.Where(notice => notice.EventType == NotificationEventType.FaqAnswered).ToArray();
            Assert.Equal(new[] { firstId, secondId }.Order(), studentNotices.Select(notice => notice.HelpRequestId!.Value).Order());
            Assert.Equal(2, studentNotices.Select(notice => notice.UserId).Distinct().Count());
            Assert.All(studentNotices, notice =>
            {
                Assert.Equal(session.Id, notice.SessionId);
                Assert.Equal(1, notice.Generation);
                Assert.Equal("AwaitingConfirmation", notice.Status);
            });
            await using var verification = app.Services.CreateAsyncScope();
            var faq = await verification.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.SingleAsync();
            Assert.Equal("Shared answer", faq.Answer);
            Assert.Equal(2, faq.HelpRequestIds.Count);
        }
        finally
        {
            // Bindings must not leak into other tests that deliberately publish without a bound route.
            var factory = new ConnectionFactory { Uri = new Uri(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")!) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            foreach (var suffix in new[] { "-created", "-answered", "-updates", "-originals", "-notifications" })
                await channel.QueueDeleteAsync(prefix + suffix);
        }
    }

    private sealed class FailingApplyGeneralizationHandler : ICommandHandler<ApplyGeneralizationCommand, bool>
    {
        public Task<bool> ExecuteAsync(ApplyGeneralizationCommand command, CancellationToken cancellationToken) =>
            throw new IOException("Simulated transient failure while applying the generalization to help requests.");
    }
}
