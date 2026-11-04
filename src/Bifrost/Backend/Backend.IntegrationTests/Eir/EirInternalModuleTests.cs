using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using EirService.Contracts.HelpRequests.GetFaqAnswerRecipients;
using EirService.Contracts.HelpRequests.HandleSessionChange;
using EirService.Contracts.KnowledgeBase.RecordFaqAnswer;
using EirService.Contracts.Messaging;
using EirService.Contracts.Participants.GetSessionParticipantIds;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.HelpRequests.Application.Features.Internal.GetFaqAnswerRecipients;
using EirService.HelpRequests.Application.Features.Internal.HelpRequestChanged;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.HelpRequests.Infrastructure.Repositories;
using EirService.KnowledgeBase.Application.Features.Internal.FaqAnswerRecorded;
using EirService.KnowledgeBase.Application.Features.Internal.RecordFaqAnswer;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.KnowledgeBase.Infrastructure.Persistence;
using EirService.KnowledgeBase.Infrastructure.Repositories;
using EirService.Sessions.Application.Features.Internal.SessionArchived;
using EirService.Sessions.Application.Features.Internal.SessionCleared;
using EirService.Sessions.Application.Features.Internal.SessionDeleted;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Domain.DomainEvents;
using EirService.Sessions.Infrastructure.Persistence;
using EirService.Sessions.Infrastructure.Repositories;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Application.Persistence;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Infrastructure.Persistence.UnitOfWork;

namespace Backend.IntegrationTests.Eir;

// FastEndpoints event registrations are process-global, as in the other component hosts.
[Collection("Backend hosts")]
public sealed class EirInternalModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FaqAnswerReachesKnowledgeBaseWithoutRabbitMqAndNotificationsFollowItsCommit()
    {
        await using var fixture = await ModuleFixture.CreateAsync();
        var command = await fixture.AnswerFaqAsync();
        Assert.Null((await fixture.ReadFaqAsync()).Answer);
        Assert.Empty(await fixture.ReadNotificationsAsync());
        Assert.Null((await fixture.ReadInternalAnswerAsync()).PublishedAtUtc);

        // The external worker must neither consume the local command nor hold it hostage to RabbitMQ.
        using var external = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(fixture.Services);
        await external.PublishPendingAsync(default);
        Assert.DoesNotContain(fixture.Bus.Attempts, route => route.Exchange == InternalMessageRoutes.Exchange);
        Assert.Null((await fixture.ReadInternalAnswerAsync()).PublishedAtUtc);

        using var local = fixture.AnswerDispatcher();
        await local.DispatchPendingAsync(default);

        var faq = await fixture.ReadFaqAsync();
        Assert.Equal((command.Answer, command.TeacherId, command.AnsweredAt),
            (faq.Answer, faq.AnsweredByTeacherId, faq.AnsweredAt));
        Assert.NotNull((await fixture.ReadInternalAnswerAsync()).PublishedAtUtc);
        var notifications = await fixture.ReadNotificationsAsync();
        Assert.Equal(new[] { "student", "teacher" }, notifications.Select(row => ReadNotice(row).UserId).Order());
        Assert.All(notifications, row => Assert.Null(row.PublishedAtUtc));
        var student = ReadNotice(Assert.Single(notifications, row => ReadNotice(row).UserId == "student"));
        Assert.Equal("FaqSuggested", student.Change);
        Assert.Equal(fixture.StudentRequestId, student.HelpRequestId);

        using var notificationWorker = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirKnowledgeBaseDbContext>>(fixture.Services);
        await notificationWorker.PublishPendingAsync(default);
        Assert.All(await fixture.ReadNotificationsAsync(), row => Assert.Null(row.PublishedAtUtc));
        Assert.DoesNotContain(fixture.Bus.Attempts, route => route.Exchange == InternalMessageRoutes.Exchange);
    }

    [Fact]
    public async Task UncommittedSenderChangesAreInvisibleAndFailedCommitDoesNotDeliverAnAnswer()
    {
        await using var fixture = await ModuleFixture.CreateAsync();
        await using var sender = fixture.Services.CreateAsyncScope();
        var db = sender.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var request = await db.Requests.Include(value => value.Messages).SingleAsync(value => value.Id == fixture.FaqId);
        request.Answer("teacher", new MessageText("Answer"), Now.AddMinutes(1));
        using var local = fixture.AnswerDispatcher();

        await local.DispatchPendingAsync(default);
        Assert.Null((await fixture.ReadFaqAsync()).Answer);
        Assert.Empty(await fixture.ReadNotificationsAsync());

        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_sender BEFORE INSERT ON outbox BEGIN SELECT RAISE(ABORT, 'sender commit failed'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => sender.ServiceProvider.GetRequiredService<IUnitOfWork<IHelpRequestRepository>>().SaveChangesAsync());
        await local.DispatchPendingAsync(default);
        Assert.Null((await fixture.ReadFaqAsync()).Answer);
        Assert.Empty(await fixture.ReadNotificationsAsync());
        await using var verification = fixture.Services.CreateAsyncScope();
        var persisted = verification.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        Assert.Empty(await persisted.Outbox.ToArrayAsync());
        Assert.Equal(HelpRequestStatus.Pending, (await persisted.Requests.SingleAsync(value => value.Id == fixture.FaqId)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiverFailureRollsBackTheAnswerAndNotificationsAndRetriesInAFreshScope(bool failNotificationInsert)
    {
        await using var fixture = await ModuleFixture.CreateAsync();
        await fixture.AnswerFaqAsync();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>();
            var trigger = failNotificationInsert
                ? "CREATE TRIGGER reject_receiver BEFORE INSERT ON outbox BEGIN SELECT RAISE(ABORT, 'notification commit failed'); END;"
                : "CREATE TRIGGER reject_receiver BEFORE UPDATE ON FaqAnswer BEGIN SELECT RAISE(ABORT, 'answer commit failed'); END;";
            await db.Database.ExecuteSqlRawAsync(trigger);
        }

        using var local = fixture.AnswerDispatcher();
        await local.DispatchPendingAsync(default);
        Assert.Null((await fixture.ReadFaqAsync()).Answer);
        Assert.Empty(await fixture.ReadNotificationsAsync());
        var failed = await fixture.ReadInternalAnswerAsync();
        Assert.Null(failed.PublishedAtUtc);
        Assert.NotNull(failed.LastAttemptAtUtc);

        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Database.ExecuteSqlRawAsync("DROP TRIGGER reject_receiver;");
        await local.DispatchPendingAsync(default);

        Assert.Equal("Answer", (await fixture.ReadFaqAsync()).Answer);
        Assert.Equal(2, (await fixture.ReadNotificationsAsync()).Length);
        var delivered = await fixture.ReadInternalAnswerAsync();
        Assert.Equal(failed.MessageId, delivered.MessageId);
        Assert.NotNull(delivered.PublishedAtUtc);
    }

    [Fact]
    public async Task RetryAfterReceiverCommitPreservesTheFirstAnswerAndDoesNotDuplicateNotifications()
    {
        await using var fixture = await ModuleFixture.CreateAsync();
        await fixture.AnswerFaqAsync();
        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_ack BEFORE UPDATE OF PublishedAtUtc ON outbox WHEN NEW.PublishedAtUtc IS NOT NULL BEGIN SELECT RAISE(ABORT, 'sender acknowledgement failed'); END;");
        using var local = fixture.AnswerDispatcher();
        await local.DispatchPendingAsync(default);

        var faq = await fixture.ReadFaqAsync();
        Assert.Equal("Answer", faq.Answer);
        var firstNotifications = await fixture.ReadNotificationsAsync();
        Assert.Equal(2, firstNotifications.Length);
        Assert.Null((await fixture.ReadInternalAnswerAsync()).PublishedAtUtc);

        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Database.ExecuteSqlRawAsync("DROP TRIGGER reject_ack;");
        await local.DispatchPendingAsync(default);
        await local.DispatchPendingAsync(default);

        Assert.Equal(faq.AnsweredAt, (await fixture.ReadFaqAsync()).AnsweredAt);
        Assert.Equal(firstNotifications.Select(row => row.Id).Order(), (await fixture.ReadNotificationsAsync()).Select(row => row.Id).Order());
        Assert.NotNull((await fixture.ReadInternalAnswerAsync()).PublishedAtUtc);
    }

    [Fact]
    public async Task DelayedLocalAnswerRefreshesTheArchiveWithoutPromptingAnOldConfirmation()
    {
        await using var fixture = await ModuleFixture.CreateAsync();
        await fixture.AnswerFaqAsync();
        fixture.State.Generation = 2;

        using var local = fixture.AnswerDispatcher();
        await local.DispatchPendingAsync(default);

        Assert.Equal("Answer", (await fixture.ReadFaqAsync()).Answer);
        var notifications = await fixture.ReadNotificationsAsync();
        Assert.Equal(2, notifications.Length);
        Assert.All(notifications, row =>
        {
            var notice = ReadNotice(row);
            Assert.Equal("ArchivedFaqAnswered", notice.Change);
            Assert.Equal(2, notice.Generation);
            Assert.Null(notice.HelpRequestId);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidInternalRouteOrCommandTypeRemainsPendingWithoutBlockingValidDeliveryOrLeakingToRabbitMq(bool unknownRoute)
    {
        await using var fixture = await ModuleFixture.CreateAsync();
        var command = await fixture.AnswerFaqAsync();
        var invalidId = Guid.NewGuid();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            db.Outbox.Add(OutboxMessage.Create(OutgoingMessage.Internal(invalidId, command,
                unknownRoute ? "eir.unregistered" : EirModuleRoutes.SessionChanged), Now));
            await db.SaveChangesAsync();
        }

        using var local = fixture.AnswerDispatcher();
        await local.DispatchPendingAsync(default);
        Assert.Equal("Answer", (await fixture.ReadFaqAsync()).Answer);
        Assert.Equal(2, (await fixture.ReadNotificationsAsync()).Length);
        Assert.Empty(fixture.State.SessionChanges);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox
                .SingleAsync(value => value.MessageId == invalidId);
            Assert.Null(row.PublishedAtUtc);
            Assert.NotNull(row.LastAttemptAtUtc);
        }

        using var external = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(fixture.Services);
        await external.PublishPendingAsync(default);
        Assert.DoesNotContain(fixture.Bus.Attempts, route => route.Exchange == InternalMessageRoutes.Exchange);
    }

    [Fact]
    public async Task CancellationDuringInternalDeliveryLeavesTheCommandPendingForTheNextRun()
    {
        await using var fixture = await ModuleFixture.CreateAsync();
        var command = new HandleSessionChangeCommand(Guid.NewGuid(), fixture.SessionId, 2, SessionChangeType.Cleared, Now, 1);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            db.Outbox.Add(OutboxMessage.Create(OutgoingMessage.Internal(command.EventId, command, EirModuleRoutes.SessionChanged), Now));
            await db.SaveChangesAsync();
        }
        using var cancellation = new CancellationTokenSource();
        fixture.State.CancelDuringSessionChange = cancellation;
        using var local = ActivatorUtilities.CreateInstance<InternalOutboxDispatcher<EirSessionDbContext>>(fixture.Services);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => local.DispatchPendingAsync(cancellation.Token));

        Assert.Empty(fixture.State.SessionChanges);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Outbox.SingleAsync();
            Assert.Null(row.PublishedAtUtc);
            Assert.Null(row.LastAttemptAtUtc);
        }
        fixture.State.CancelDuringSessionChange = null;
        await local.DispatchPendingAsync(default);
        Assert.Equal(command, Assert.Single(fixture.State.SessionChanges));
        Assert.Empty(fixture.Bus.Attempts);
    }

    [Theory]
    [InlineData(SessionChangeType.Cleared)]
    [InlineData(SessionChangeType.Archived)]
    [InlineData(SessionChangeType.Deleted)]
    public async Task SessionLifecycleUsesLocalDeliveryAndRetainsTheExternalMimirEvent(SessionChangeType change)
    {
        await using var fixture = await ModuleFixture.CreateAsync();
        var outgoing = new List<OutgoingMessage>();
        var scopeFactory = fixture.Services.GetRequiredService<IServiceScopeFactory>();
        Guid eventId;
        if (change == SessionChangeType.Cleared)
        {
            var domainEvent = new SessionClearedDomainEvent(fixture.SessionId, 1, 2, Now);
            eventId = domainEvent.Id;
            await new SessionClearedIntegrationEventUpgradeHandler(scopeFactory)
                .HandleAsync(new(domainEvent, outgoing.Add), default);
        }
        else if (change == SessionChangeType.Archived)
        {
            var domainEvent = new SessionArchivedDomainEvent(fixture.SessionId, 1, 2, Now);
            eventId = domainEvent.Id;
            await new SessionArchivedIntegrationEventUpgradeHandler(scopeFactory)
                .HandleAsync(new(domainEvent, outgoing.Add), default);
        }
        else
        {
            var domainEvent = new SessionDeletedDomainEvent(fixture.SessionId, 1, Now);
            eventId = domainEvent.Id;
            await new SessionDeletedIntegrationEventUpgradeHandler(scopeFactory)
                .HandleAsync(new(domainEvent, outgoing.Add), default);
        }
        var localMessage = Assert.Single(outgoing, message => message.ExchangeName == InternalMessageRoutes.Exchange);
        Assert.Equal(EirModuleRoutes.SessionChanged, localMessage.RoutingKey);
        var externalMessage = Assert.Single(outgoing, message => message.RoutingKey == MessageRoutes.SessionUpdated);
        Assert.Equal(eventId, externalMessage.Id);
        Assert.IsType<SessionUpdatedIntegrationEvent>(externalMessage.Payload);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            db.Outbox.AddRange(outgoing.Select(message => OutboxMessage.Create(message, Now)));
            await db.SaveChangesAsync();
        }

        using var local = ActivatorUtilities.CreateInstance<InternalOutboxDispatcher<EirSessionDbContext>>(fixture.Services);
        await local.DispatchPendingAsync(default);
        var received = Assert.Single(fixture.State.SessionChanges);
        Assert.Equal((eventId, fixture.SessionId, change), (received.EventId, received.SessionId, received.EventType));
        Assert.Empty(fixture.Bus.Attempts);

        // The separately retained integration event is still available for Mimir's RabbitMQ consumer.
        await using var verification = fixture.Services.CreateAsyncScope();
        var rows = await verification.ServiceProvider.GetRequiredService<EirSessionDbContext>().Outbox.AsNoTracking().ToArrayAsync();
        Assert.NotNull(Assert.Single(rows, row => row.ExchangeName == InternalMessageRoutes.Exchange).PublishedAtUtc);
        Assert.Null(Assert.Single(rows, row => row.RoutingKey == MessageRoutes.SessionUpdated).PublishedAtUtc);
    }

    private static NotificationCreatedMessage ReadNotice(OutboxMessage row) =>
        JsonSerializer.Deserialize<NotificationCreatedMessage>(row.Payload, JsonOptions)!;

    private sealed class ModuleFixture : IAsyncDisposable
    {
        private readonly SqliteTestDatabase requests = new();
        private readonly SqliteTestDatabase knowledge = new();
        private readonly SqliteTestDatabase sessions = new();
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid FaqId { get; } = Guid.NewGuid();
        public Guid StudentRequestId { get; private set; }
        public ModuleState State { get; } = new();
        public UnavailableBus Bus { get; } = new();
        public ServiceProvider Services { get; private set; } = null!;

        public static async Task<ModuleFixture> CreateAsync()
        {
            var fixture = new ModuleFixture();
            try
            {
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton(fixture.State);
                services.AddSingleton<IMessageBus>(fixture.Bus);
                fixture.requests.Register<EirRequestDbContext>(services,
                    options => options.ReplaceService<IModelCustomizer, EirPersistenceModelCustomizer>());
                fixture.knowledge.Register<EirKnowledgeBaseDbContext>(services,
                    options => options.ReplaceService<IModelCustomizer, EirPersistenceModelCustomizer>());
                fixture.sessions.Register<EirSessionDbContext>(services);
                services.AddScoped<IHelpRequestRepository, HelpRequestRepository>();
                // Generic UoW exercises production events and atomic persistence. PostgreSQL advisory locks
                // and number-allocation triggers are intentionally outside this SQLite component test.
                services.AddUnitOfWork<IHelpRequestRepository, EirRequestDbContext>();
                services.AddScoped<IFaqRepository, FaqRepository>();
                services.AddScoped<IUnitOfWork<IFaqRepository>, KnowledgeBaseUnitOfWork>();
                services.AddScoped<ISessionRepository, SessionRepository>();
                services.AddScoped<ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>>(_ =>
                    new SessionQuery(fixture.SessionId, fixture.State));
                services.AddScoped<ICommandHandler<GetSessionParticipantIdsQuery, string[]>, ParticipantQuery>();
                services.AddScoped<ICommandHandler<GetFaqAnswerRecipientsQuery, IReadOnlyList<FaqAnswerRecipient>>, GetFaqAnswerRecipientsQueryHandler>();
                services.AddScoped<ICommandHandler<RecordFaqAnswerCommand>, RecordFaqAnswerCommandHandler>();
                services.AddScoped<ICommandHandler<HandleSessionChangeCommand>, SessionChangeRecorder>();
                services.AddInternalCommand<RecordFaqAnswerCommand>(EirModuleRoutes.FaqAnswered);
                services.AddInternalCommand<HandleSessionChangeCommand>(EirModuleRoutes.SessionChanged);
                services.AddMessaging(new List<Type>
                {
                    typeof(HelpRequestChangedIntegrationEventHandler), typeof(FaqAnswerRecordedIntegrationEventHandler)
                });
                fixture.Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                fixture.Services.UseMessaging();
                await using var scope = fixture.Services.CreateAsyncScope();
                var requestDb = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
                var faqDb = scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>();
                await requestDb.Database.EnsureCreatedAsync();
                await faqDb.Database.EnsureCreatedAsync();
                await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Database.EnsureCreatedAsync();
                var student = HelpRequest.Create(fixture.SessionId, 1, "student", HelpType.Physical, new("Original question"), Now);
                student.LinkToFaq(fixture.FaqId, new("Shared question"), Now);
                student.ClearDomainEvents();
                fixture.StudentRequestId = student.Id;
                var faqRequest = HelpRequest.CreateFaq(fixture.FaqId, fixture.SessionId, 1, new("Shared question"), Now);
                faqRequest.Reserve("teacher", Now);
                faqRequest.ClearDomainEvents();
                requestDb.Requests.AddRange(student, faqRequest);
                requestDb.Entry(student).Property(request => request.RequestNumber).CurrentValue = 1;
                requestDb.Entry(faqRequest).Property(request => request.RequestNumber).CurrentValue = 2;
                await requestDb.SaveChangesAsync();
                faqDb.Faqs.Add(FaqAnswer.Create(fixture.FaqId, fixture.SessionId, 1, "Shared question", [student.Id, Guid.NewGuid()], Now));
                await faqDb.SaveChangesAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task<RecordFaqAnswerCommand> AnswerFaqAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            var request = await db.Requests.Include(value => value.Messages).SingleAsync(value => value.Id == FaqId);
            request.Answer("teacher", new("Answer"), Now.AddMinutes(1));
            var student = await db.Requests.Include(value => value.Messages).SingleAsync(value => value.Id == StudentRequestId);
            student.SuggestFaq(FaqId, "teacher", new("Answer"), Now.AddMinutes(1));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork<IHelpRequestRepository>>().SaveChangesAsync();
            var row = await db.Outbox.SingleAsync(value => value.RoutingKey == EirModuleRoutes.FaqAnswered);
            return JsonSerializer.Deserialize<RecordFaqAnswerCommand>(row.Payload, JsonOptions)!;
        }

        public InternalOutboxDispatcher<EirRequestDbContext> AnswerDispatcher() =>
            ActivatorUtilities.CreateInstance<InternalOutboxDispatcher<EirRequestDbContext>>(Services);

        public async Task<FaqAnswer> ReadFaqAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Faqs.AsNoTracking().SingleAsync();
        }

        public async Task<OutboxMessage> ReadInternalAnswerAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.AsNoTracking()
                .SingleAsync(row => row.RoutingKey == EirModuleRoutes.FaqAnswered);
        }

        public async Task<OutboxMessage[]> ReadNotificationsAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<EirKnowledgeBaseDbContext>().Outbox.AsNoTracking()
                .Where(row => row.RoutingKey == MessageRoutes.NotificationCreated).ToArrayAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (Services is not null) await Services.DisposeAsync();
            await requests.DisposeAsync();
            await knowledge.DisposeAsync();
            await sessions.DisposeAsync();
        }
    }

    private sealed class ModuleState
    {
        public int Generation { get; set; } = 1;
        public List<HandleSessionChangeCommand> SessionChanges { get; } = [];
        public CancellationTokenSource? CancelDuringSessionChange { get; set; }
    }

    private sealed class SessionQuery(Guid sessionId, ModuleState state) : ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>
    {
        public Task<HelpRequestSession?> ExecuteAsync(GetHelpRequestSessionQuery command, CancellationToken ct) =>
            Task.FromResult(command.SessionId == sessionId ? new HelpRequestSession(sessionId, state.Generation, ["teacher"]) : null);
    }

    private sealed class ParticipantQuery : ICommandHandler<GetSessionParticipantIdsQuery, string[]>
    {
        public Task<string[]> ExecuteAsync(GetSessionParticipantIdsQuery command, CancellationToken ct) => Task.FromResult(new[] { "student" });
    }

    private sealed class SessionChangeRecorder(ModuleState state) : ICommandHandler<HandleSessionChangeCommand>
    {
        public Task ExecuteAsync(HandleSessionChangeCommand command, CancellationToken ct)
        {
            state.CancelDuringSessionChange?.Cancel();
            ct.ThrowIfCancellationRequested();
            state.SessionChanges.Add(command);
            return Task.CompletedTask;
        }
    }

    private sealed class UnavailableBus : IMessageBus
    {
        public List<(string Exchange, string Route)> Attempts { get; } = [];
        public Task PublishAsync(Guid id, object payload, string exchangeName, string routingKey,
            CancellationToken cancellationToken = default, string? messageType = null)
        {
            Attempts.Add((exchangeName, routingKey));
            throw new IOException("RabbitMQ is unavailable in this component test.");
        }

        public Task<IAsyncDisposable> SubscribeAsync<T>(string queueName, string exchangeName, string routingKey,
            Func<MessageEnvelope<T>, CancellationToken, Task> handler, CancellationToken cancellationToken = default) =>
            throw new IOException("This fixture deliberately has no RabbitMQ server.");
    }
}
