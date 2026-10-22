using EirService.Contracts.HelpRequests.ApplyGeneralization;
using EirService.Contracts.HelpRequests.CanApplyGeneralization;
using EirService.Contracts.Sessions.GetSessionGenerations;
using EirService.HelpRequests.Application;
using EirService.HelpRequests.Infrastructure;
using EirService.KnowledgeBase.Application;
using EirService.KnowledgeBase.Application.Features.Internal.RegisterGeneralization;
using EirService.KnowledgeBase.Application.Repositories;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.KnowledgeBase.Infrastructure;
using EirService.Sessions.Infrastructure;
using FastEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;

namespace EirService.Test;

public sealed class RegisterGeneralizationMessageHandlerTests
{
    [Fact]
    public async Task NullPayloadIsDiscardedBeforeCallingOtherModules()
    {
        var fixture = new HandlerFixture();

        await fixture.DeliverAsync(null!);

        Assert.Empty(fixture.Sessions.Commands);
        Assert.Empty(fixture.Sources.Commands);
        Assert.Empty(fixture.Apply.Commands);
        Assert.Empty(fixture.Faqs.Saved);
        Assert.Equal(0, fixture.Faqs.Commits);
    }

    [Theory]
    [InlineData("empty generalization id")]
    [InlineData("empty session id")]
    [InlineData("invalid generation")]
    [InlineData("missing sources")]
    [InlineData("one source")]
    [InlineData("duplicate sources")]
    [InlineData("too many sources")]
    [InlineData("empty source id")]
    [InlineData("self reference")]
    [InlineData("blank question")]
    [InlineData("long question")]
    public async Task MalformedMessagesAreDiscardedBeforeCallingOtherModules(string invalidPart)
    {
        var fixture = new HandlerFixture();
        var payload = fixture.Payload;
        payload = invalidPart switch
        {
            "empty generalization id" => payload with { GeneralizationId = Guid.Empty },
            "empty session id" => payload with { SessionId = Guid.Empty },
            "invalid generation" => payload with { Generation = 0 },
            "missing sources" => payload with { HelpRequestIds = null! },
            "one source" => payload with { HelpRequestIds = [payload.HelpRequestIds[0]] },
            "duplicate sources" => payload with { HelpRequestIds = [payload.HelpRequestIds[0], payload.HelpRequestIds[0]] },
            "too many sources" => payload with { HelpRequestIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() },
            "empty source id" => payload with { HelpRequestIds = [Guid.Empty, payload.HelpRequestIds[0]] },
            "self reference" => payload with { HelpRequestIds = [payload.GeneralizationId, payload.HelpRequestIds[0]] },
            "blank question" => payload with { Question = "  " },
            "long question" => payload with { Question = new string('?', 4001) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidPart))
        };

        await fixture.DeliverAsync(payload);

        Assert.Empty(fixture.Sessions.Commands);
        Assert.Empty(fixture.Sources.Commands);
        Assert.Empty(fixture.Apply.Commands);
        Assert.Empty(fixture.Faqs.Saved);
        Assert.Equal(0, fixture.Faqs.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrObsoleteSessionDoesNotRegisterOrApplyTheGeneralization(bool obsolete)
    {
        var fixture = new HandlerFixture();
        fixture.Sessions.Result = obsolete
            ? new Dictionary<Guid, int> { [fixture.Payload.SessionId] = fixture.Payload.Generation + 1 }
            : new Dictionary<Guid, int>();

        await fixture.DeliverAsync(fixture.Payload);

        Assert.Single(fixture.Sessions.Commands);
        Assert.Empty(fixture.Sources.Commands);
        Assert.Empty(fixture.Apply.Commands);
        Assert.Empty(fixture.Faqs.Saved);
    }

    [Fact]
    public async Task InvalidSourceRequestsDoNotCreateACanonicalFaq()
    {
        var fixture = new HandlerFixture();
        fixture.Sources.Result = false;

        await fixture.DeliverAsync(fixture.Payload);

        Assert.Single(fixture.Sources.Commands);
        Assert.Empty(fixture.Apply.Commands);
        Assert.Empty(fixture.Faqs.Saved);
        Assert.Equal(0, fixture.Faqs.Commits);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("sources")]
    [InlineData("session")]
    [InlineData("generation")]
    public async Task ConflictingCanonicalGeneralizationDoesNotChangeEitherModule(string conflict)
    {
        var fixture = new HandlerFixture();
        var payload = fixture.Payload;
        var canonical = FaqAnswer.Create(payload.GeneralizationId,
            conflict == "session" ? Guid.NewGuid() : payload.SessionId,
            conflict == "generation" ? payload.Generation + 1 : payload.Generation,
            conflict == "question" ? "A different question" : payload.Question,
            conflict == "sources" ? [Guid.NewGuid(), Guid.NewGuid()] : payload.HelpRequestIds,
            DateTimeOffset.UtcNow);
        fixture.Faqs.Saved.Add(canonical);

        await fixture.DeliverAsync(payload);

        Assert.Same(canonical, Assert.Single(fixture.Faqs.Saved));
        Assert.Empty(fixture.Sources.Commands);
        Assert.Empty(fixture.Apply.Commands);
        Assert.Equal(0, fixture.Faqs.Commits);
    }

    [Fact]
    public async Task FailureAfterCanonicalCommitIsRetriedWithoutWritingTheFaqAgain()
    {
        var fixture = new HandlerFixture();
        fixture.Apply.BeforeExecute = _ =>
        {
            Assert.Equal(1, fixture.Faqs.Commits);
            Assert.Equal(fixture.Payload.GeneralizationId, Assert.Single(fixture.Faqs.Saved).Id);
        };
        fixture.Apply.Failure = new IOException("The help request module is temporarily unavailable.");

        await Assert.ThrowsAsync<IOException>(() => fixture.DeliverAsync(fixture.Payload));

        Assert.Single(fixture.Faqs.Saved);
        Assert.Equal(1, fixture.Faqs.Commits);
        fixture.Apply.Failure = null;
        await fixture.DeliverAsync(fixture.Payload);
        await fixture.DeliverAsync(fixture.Payload);

        Assert.Equal(3, fixture.Apply.Commands.Count);
        Assert.Single(fixture.Faqs.Saved);
        Assert.Equal(1, fixture.Faqs.Commits);
    }

    [Fact]
    public async Task ReorderedRepeatedSourcesAndWhitespaceAreTheSameCanonicalGeneralization()
    {
        var fixture = new HandlerFixture();
        await fixture.DeliverAsync(fixture.Payload);

        await fixture.DeliverAsync(fixture.Payload with
        {
            Question = "  " + fixture.Payload.Question + "  ",
            HelpRequestIds = [fixture.Payload.HelpRequestIds[1], fixture.Payload.HelpRequestIds[0], fixture.Payload.HelpRequestIds[1]]
        });

        Assert.Equal(1, fixture.Faqs.Commits);
        Assert.Single(fixture.Faqs.Saved);
        Assert.Equal(2, fixture.Apply.Commands.Count);
    }

    [Fact]
    public async Task ModuleContractsAndIncomingHandlerResolveInAnIndependentConsumerScope()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:eir"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
            ["ConnectionStrings:rabbitmq"] = "amqp://unused-in-tests"
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddHelpRequestsInfrastructure(configuration);
        services.AddHelpRequestsApplication();
        services.AddSessionsInfrastructure(configuration);
        services.AddKnowledgeBaseInfrastructure(configuration);
        services.AddKnowledgeBaseApplication();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<RegisterGeneralizationMessageHandler>());
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var handler = first.ServiceProvider.GetRequiredService<RegisterGeneralizationMessageHandler>();
        Assert.Same(handler, first.ServiceProvider.GetRequiredService<RegisterGeneralizationMessageHandler>());
        Assert.NotSame(handler, second.ServiceProvider.GetRequiredService<RegisterGeneralizationMessageHandler>());
        Assert.NotNull(first.ServiceProvider.GetRequiredService<ICommandHandler<GetSessionGenerationsQuery, IReadOnlyDictionary<Guid, int>>>());
        Assert.NotNull(first.ServiceProvider.GetRequiredService<ICommandHandler<CanApplyGeneralizationQuery, bool>>());
        Assert.NotNull(first.ServiceProvider.GetRequiredService<ICommandHandler<ApplyGeneralizationCommand, bool>>());
    }

    private sealed class HandlerFixture
    {
        public GeneralizedCreatedMessage Payload { get; } = new(Guid.NewGuid(), Guid.NewGuid(), 1,
            "How does the shared question work?", [Guid.NewGuid(), Guid.NewGuid()]);
        public MemoryFaqs Faqs { get; } = new();
        public RecordingHandler<GetSessionGenerationsQuery, IReadOnlyDictionary<Guid, int>> Sessions { get; }
        public RecordingHandler<CanApplyGeneralizationQuery, bool> Sources { get; } = new(true);
        public RecordingHandler<ApplyGeneralizationCommand, bool> Apply { get; } = new(true);
        private RegisterGeneralizationMessageHandler Handler { get; }

        public HandlerFixture()
        {
            Sessions = new(new Dictionary<Guid, int> { [Payload.SessionId] = Payload.Generation });
            Handler = new(NullLogger<RegisterGeneralizationMessageHandler>.Instance, Faqs, Faqs, TimeProvider.System,
                Sessions, Sources, Apply);
        }

        public Task DeliverAsync(GeneralizedCreatedMessage payload) =>
            Handler.HandleAsync(new MessageEnvelope<GeneralizedCreatedMessage>(Guid.NewGuid(), payload), CancellationToken.None);
    }

    private sealed class RecordingHandler<TCommand, TResult>(TResult result) : ICommandHandler<TCommand, TResult>
        where TCommand : ICommand<TResult>
    {
        public TResult Result { get; set; } = result;
        public Exception? Failure { get; set; }
        public Action<TCommand>? BeforeExecute { get; set; }
        public List<TCommand> Commands { get; } = [];

        public Task<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            BeforeExecute?.Invoke(command);
            return Failure is null ? Task.FromResult(Result) : Task.FromException<TResult>(Failure);
        }
    }

    private sealed class MemoryFaqs : IFaqRepository, IUnitOfWork<IFaqRepository>
    {
        private readonly List<FaqAnswer> pending = [];
        public List<FaqAnswer> Saved { get; } = [];
        public int Commits { get; private set; }

        public Task<FaqAnswer?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Saved.SingleOrDefault(faq => faq.Id == id));

        public Task AddAsync(FaqAnswer faq, CancellationToken cancellationToken)
        {
            pending.Add(faq);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FaqAnswer>> GetForSessionAsync(Guid sessionId, int generation, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FaqAnswer>>(Saved.Where(faq => faq.SessionId == sessionId && faq.Generation == generation).ToArray());

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Commits++;
            Saved.AddRange(pending);
            pending.Clear();
            return Task.CompletedTask;
        }
    }
}
