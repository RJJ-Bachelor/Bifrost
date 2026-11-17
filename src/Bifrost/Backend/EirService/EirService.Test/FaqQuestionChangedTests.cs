using EirService.KnowledgeBase.Application.Features.Internal.FaqQuestionChanged;
using EirService.KnowledgeBase.Domain.DomainEvents;
using EirService.KnowledgeBase.Domain.Entities;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.Test;

public sealed class FaqQuestionChangedTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T10:00:00Z");

    [Fact]
    public async Task RevisedQuestionStagesItsOriginalTextAndAnswerForMimir()
    {
        var faq = CreateFaq();
        faq.RecordAnswer(" Answer ", "teacher", Now, " Revised question ");
        var change = Assert.Single(faq.GetDomainEvents().OfType<FaqQuestionChangedDomainEvent>());
        var outgoing = new List<OutgoingMessage>();

        await new FaqQuestionChangedIntegrationEventHandler().HandleAsync(new(change, outgoing.Add), default);

        var message = Assert.Single(outgoing);
        Assert.Equal(new FaqQuestionChangedIntegrationEvent(faq.Id, faq.SessionId, faq.Generation,
            "Question", "Revised question", "Answer", "teacher", Now), message.Payload);
        Assert.Equal(MessageRoutes.Requests, message.ExchangeName);
        Assert.Equal(MessageRoutes.FaqQuestionChanged, message.RoutingKey);
        Assert.Equal("faq.question-changed", message.Metadata!.Type);
        Assert.Equal(1, message.Metadata.Version);
        Assert.Equal(change.Id, message.Metadata.CausationId);
        Assert.Equal(Now, message.Metadata.OccurredAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Question")]
    [InlineData(" Question ")]
    public void UnchangedQuestionOnlyRecordsTheAnswer(string? question)
    {
        var faq = CreateFaq();

        faq.RecordAnswer("Answer", "teacher", Now, question);

        Assert.IsType<FaqAnswerRecordedDomainEvent>(Assert.Single(faq.GetDomainEvents()));
    }

    private static FaqAnswer CreateFaq() => FaqAnswer.Create(Guid.NewGuid(), Guid.NewGuid(), 1,
        "Question", [Guid.NewGuid(), Guid.NewGuid()], Now);
}
