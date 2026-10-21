using Microsoft.Extensions.Logging.Abstractions;
using MimirService.Application.Repositories;
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
        var handler = new HelpRequestMessageHandler(NullLogger<HelpRequestMessageHandler>.Instance, repository);
        var message = Message();
        await handler.HandleAsync(message, CancellationToken.None);
        var (request, reply) = Assert.Single(repository.Added);
        Assert.Equal(message.Payload.HelpRequestId, request.Id);
        Assert.Equal(message.Payload.SessionId, request.SessionId);
        Assert.Equal(message.Payload.Generation, request.Generation);
        Assert.Equal(message.Payload.Message, request.Message);
        Assert.Null(reply);
    }

    [Fact]
    public async Task RedeliveryDoesNotCreateAnotherRequest()
    {
        var repository = new FakeRepository();
        var handler = new HelpRequestMessageHandler(NullLogger<HelpRequestMessageHandler>.Instance, repository);
        var message = Message();
        await handler.HandleAsync(message, CancellationToken.None);
        await handler.HandleAsync(message, CancellationToken.None);
        Assert.Single(repository.Added);
    }

    private static MessageEnvelope<HelpRequestCreatedMessage> Message() =>
        new(Guid.NewGuid(), new HelpRequestCreatedMessage(Guid.NewGuid(), Guid.NewGuid(), 1, "Help me"));

    private sealed class FakeRepository : IHelpRequestRepository
    {
        public HashSet<Guid> ExistingIds { get; } = [];
        public List<(HelpRequest Request, OutgoingMessage? Reply)> Added { get; } = [];
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(ExistingIds.Contains(id));
        public Task<bool> TryAddAsync(HelpRequest request, OutgoingMessage? reply, CancellationToken cancellationToken)
        {
            if (!ExistingIds.Add(request.Id)) return Task.FromResult(false);
            Added.Add((request, reply));
            return Task.FromResult(true);
        }
    }
}
