using Microsoft.Extensions.Logging.Abstractions;
using MimirService.Application.Repositories;
using MimirService.Application.Services.Generalizations;
using MimirService.Application.Services.Messaging;
using MimirService.Domain.Entities;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace MimirService.Test;

public sealed class HelpRequestMessageHandlerTests
{
    [Fact]
    public async Task StoresRequestWithoutPublishingAnArtificialGeneralization()
    {
        var repository = new FakeRepository();
        var handler = Handler(repository);
        var message = Message();
        await handler.HandleAsync(message, CancellationToken.None);
        var (request, generalization, reply) = Assert.Single(repository.Added);
        Assert.Equal(message.Payload.HelpRequestId, request.Id);
        Assert.Equal(message.Payload.SessionId, request.SessionId);
        Assert.Equal(message.Payload.Generation, request.Generation);
        Assert.Equal(message.Payload.Message, request.Message);
        Assert.Null(request.GeneralizationId);
        Assert.Null(generalization);
        Assert.Null(reply);
    }

    [Fact]
    public async Task RedeliveryDoesNotCreateAnotherRequest()
    {
        var repository = new FakeRepository();
        var handler = Handler(repository);
        var message = Message();
        await handler.HandleAsync(message, CancellationToken.None);
        await handler.HandleAsync(message, CancellationToken.None);
        Assert.Single(repository.Added);
    }

    [Fact]
    public async Task SecondRequestInSessionPublishesGeneralizationWithBothRequests()
    {
        var repository = new FakeRepository();
        var handler = Handler(repository);
        var sessionId = Guid.NewGuid();
        var first = Message(sessionId);
        var second = Message(sessionId);
        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(second, CancellationToken.None);

        var (request, generalization, reply) = repository.Added[1];
        Assert.NotNull(generalization);
        Assert.Equal(generalization.Id, request.GeneralizationId);
        Assert.Equal(generalization.Id, repository.Added[0].Request.GeneralizationId);
        Assert.NotNull(reply);
        Assert.Equal(generalization.Id, reply.Id);
        Assert.Equal(MessageRoutes.Requests, reply.ExchangeName);
        Assert.Equal(MessageRoutes.GeneralizedCreated, reply.RoutingKey);
        var published = Assert.IsType<GeneralizedCreatedMessage>(reply.Payload);
        Assert.Equal(generalization.Id, published.GeneralizationId);
        Assert.Equal(sessionId, published.SessionId);
        Assert.Equal(1, published.Generation);
        Assert.Equal(second.Payload.Message, published.Question);
        Assert.Equal(new[] { first.Payload.HelpRequestId, second.Payload.HelpRequestId }.Order(), published.HelpRequestIds);
    }

    [Fact]
    public async Task ThirdRequestWithTheSameTextJoinsTheGeneralization()
    {
        var repository = new FakeRepository();
        var handler = Handler(repository);
        var sessionId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
            await handler.HandleAsync(Message(sessionId), CancellationToken.None);

        var paired = repository.Added[1].Generalization!;
        var (request, generalization, reply) = repository.Added[2];
        Assert.Equal(paired.Id, request.GeneralizationId);
        Assert.Null(generalization);
        Assert.NotNull(reply);
        Assert.Equal(request.Id, reply.Id);
        Assert.Equal(MessageRoutes.Requests, reply.ExchangeName);
        Assert.Equal(MessageRoutes.GeneralizedCreated, reply.RoutingKey);
        var published = Assert.IsType<GeneralizedCreatedMessage>(reply.Payload);
        Assert.Equal(paired.Id, published.GeneralizationId);
        Assert.Equal(paired.Question, published.Question);
        Assert.Equal(repository.Added.Select(added => added.Request.Id).Order(), published.HelpRequestIds);
    }

    [Fact]
    public async Task GeneralizationsInSessionAreCandidates()
    {
        var repository = new FakeRepository();
        var sessionId = Guid.NewGuid();
        await Handler(repository).HandleAsync(Message(sessionId), CancellationToken.None);
        await Handler(repository).HandleAsync(Message(sessionId), CancellationToken.None);
        var generalization = repository.Added[1].Generalization!;

        var decider = new RecordingDecider();
        await Handler(repository, decider).HandleAsync(Message(sessionId), CancellationToken.None);

        var candidate = Assert.Single(decider.Candidates);
        Assert.Equal(new GeneralizationCandidate(generalization.Id, GeneralizationCandidateType.Generalization,
            generalization.Question), candidate);
    }

    private static HelpRequestMessageHandler Handler(FakeRepository repository) =>
        Handler(repository, new PlaceholderGeneralizationDecider());

    private static HelpRequestMessageHandler Handler(FakeRepository repository, IGeneralizationDecider decider) =>
        new(NullLogger<HelpRequestMessageHandler>.Instance, repository, decider, TimeProvider.System);

    private static MessageEnvelope<HelpRequestCreatedMessage> Message() => Message(Guid.NewGuid());

    private static MessageEnvelope<HelpRequestCreatedMessage> Message(Guid sessionId) =>
        new(Guid.NewGuid(), new HelpRequestCreatedMessage(Guid.NewGuid(), sessionId, 1, "Help me"));

    private sealed class FakeRepository : IHelpRequestRepository
    {
        public List<(HelpRequest Request, Generalization? Generalization, OutgoingMessage? Reply)> Added { get; } = [];
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Added.Any(added => added.Request.Id == id));
        public Task<IReadOnlyList<HelpRequest>> GetWaitingAsync(Guid sessionId, int generation, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HelpRequest>>(Added.Select(added => added.Request)
                .Where(request => request.SessionId == sessionId && request.Generation == generation && request.GeneralizationId is null)
                .ToArray());
        public Task<IReadOnlyList<Generalization>> GetGeneralizationsAsync(Guid sessionId, int generation,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Generalization>>(Added.Select(added => added.Generalization)
                .OfType<Generalization>()
                .Where(generalization => generalization.SessionId == sessionId && generalization.Generation == generation)
                .ToArray());
        public Task<IReadOnlyList<Guid>> GetGeneralizationMemberIdsAsync(Guid generalizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Added.Select(added => added.Request)
                .Where(request => request.GeneralizationId == generalizationId)
                .Select(request => request.Id)
                .ToArray());
        public Task<bool> TryAddAsync(HelpRequest request, Generalization? generalization, OutgoingMessage? reply,
            CancellationToken cancellationToken)
        {
            if (Added.Any(added => added.Request.Id == request.Id)) return Task.FromResult(false);
            Added.Add((request, generalization, reply));
            return Task.FromResult(true);
        }
        public Task<(int Requests, int Generalizations)> DeleteSessionDataAsync(Guid sessionId, int? beforeGeneration,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingDecider : IGeneralizationDecider
    {
        public IReadOnlyList<GeneralizationCandidate> Candidates { get; private set; } = [];

        public Task<GeneralizationDecision> DecideAsync(string newQuestion,
            IReadOnlyList<GeneralizationCandidate> candidates, CancellationToken cancellationToken)
        {
            Candidates = candidates;
            return Task.FromResult(GeneralizationDecision.Standalone);
        }
    }
}
