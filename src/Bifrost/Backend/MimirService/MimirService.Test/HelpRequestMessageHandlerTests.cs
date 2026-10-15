using Microsoft.Extensions.Logging.Abstractions;
using MimirService.Application.Repositories;
using MimirService.Application.Services.Llm;
using MimirService.Application.Services.Messaging;
using MimirService.Domain.Entities;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace MimirService.Test;

public sealed class HelpRequestMessageHandlerTests
{
    [Fact]
    public async Task NewRequestIsGeneralizedAndStoredWithReply()
    {
        var repository = new FakeRepository();
        var llm = new CountingLlmClient();
        var handler = new HelpRequestMessageHandler(NullLogger<HelpRequestMessageHandler>.Instance, repository, llm);
        var message = Message();

        await handler.HandleAsync(message, CancellationToken.None);

        Assert.Equal(1, llm.Calls);
        var (request, reply) = Assert.Single(repository.Added);
        Assert.Equal(message.Id, request.Id);
        Assert.Equal(message.Payload.SessionId, request.SessionId);
        Assert.Equal(message.Payload.Generation, request.Generation);
        var payload = Assert.IsType<GeneralizedCreatedMessage>(reply.Payload);
        Assert.Equal("Generalized answer", payload.Message);
    }

    [Fact]
    public async Task RedeliveredRequestSkipsTheLlmCall()
    {
        var message = Message();
        var repository = new FakeRepository { ExistingIds = { message.Id } };
        var llm = new CountingLlmClient();
        var handler = new HelpRequestMessageHandler(NullLogger<HelpRequestMessageHandler>.Instance, repository, llm);

        await handler.HandleAsync(message, CancellationToken.None);

        Assert.Equal(0, llm.Calls);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task UnavailableLlmForwardsTheOriginalMessage()
    {
        var repository = new FakeRepository();
        var handler = new HelpRequestMessageHandler(NullLogger<HelpRequestMessageHandler>.Instance, repository, new UnavailableLlmClient());
        var message = Message();

        await handler.HandleAsync(message, CancellationToken.None);

        var (request, reply) = Assert.Single(repository.Added);
        Assert.Equal(message.Id, request.Id);
        var payload = Assert.IsType<GeneralizedCreatedMessage>(reply.Payload);
        Assert.Equal(message.Payload.Message, payload.Message);
    }

    private static MessageEnvelope<HelpRequestCreatedMessage> Message() =>
        new(Guid.NewGuid(), new HelpRequestCreatedMessage(Guid.NewGuid(), Guid.NewGuid(), 1, "Help me"));

    private sealed class CountingLlmClient : ILlmClient
    {
        public int Calls { get; private set; }

        public Task<string> GenerateAsync(string systemInstruction, string prompt, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult("Generalized answer");
        }
    }

    private sealed class UnavailableLlmClient : ILlmClient
    {
        public Task<string> GenerateAsync(string systemInstruction, string prompt, CancellationToken cancellationToken) =>
            throw new LlmUnavailableException("All configured LLM models failed.");
    }

    private sealed class FakeRepository : IHelpRequestRepository
    {
        public HashSet<Guid> ExistingIds { get; } = [];
        public List<(HelpRequest Request, OutgoingMessage Reply)> Added { get; } = [];

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingIds.Contains(id));

        public Task<bool> TryAddAsync(HelpRequest request, OutgoingMessage reply, CancellationToken cancellationToken)
        {
            if (!ExistingIds.Add(request.Id)) return Task.FromResult(false);
            Added.Add((request, reply));
            return Task.FromResult(true);
        }
    }
}
