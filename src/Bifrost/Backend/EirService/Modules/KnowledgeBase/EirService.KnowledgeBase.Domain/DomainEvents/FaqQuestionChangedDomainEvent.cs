using EirService.KnowledgeBase.Domain.Entities;
using Shared.Domain.DomainEvents;

namespace EirService.KnowledgeBase.Domain.DomainEvents;

public sealed record FaqQuestionChangedDomainEvent(FaqAnswer Faq, string PreviousQuestion) : DomainEvent(Guid.NewGuid());
