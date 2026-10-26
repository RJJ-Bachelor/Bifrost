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
        Assert.Equal(PlaceholderGeneralizationDecider.Question, published.Question);
        Assert.Equal(new[] { first.Payload.HelpRequestId, second.Payload.HelpRequestId }.Order(), published.HelpRequestIds);
    }

    [Fact]
    public async Task ThirdRequestInSessionWaitsAlone()
    {
        var repository = new FakeRepository();
        var handler = Handler(repository);
        var sessionId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
            await handler.HandleAsync(Message(sessionId), CancellationToken.None);

        Assert.Single(repository.Added, added => added.Reply is not null);
        var (request, generalization, reply) = repository.Added[2];
        Assert.Null(request.GeneralizationId);
        Assert.Null(generalization);
        Assert.Null(reply);
    }

    private static HelpRequestMessageHandler Handler(FakeRepository repository) =>
        new(NullLogger<HelpRequestMessageHandler>.Instance, repository, new PlaceholderGeneralizationDecider(), TimeProvider.System);

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
        public Task<bool> TryAddAsync(HelpRequest request, Generalization? generalization, OutgoingMessage? reply,
            CancellationToken cancellationToken)
        {
            if (Added.Any(added => added.Request.Id == request.Id)) return Task.FromResult(false);
            Added.Add((request, generalization, reply));
            return Task.FromResult(true);
        }
    }
}
