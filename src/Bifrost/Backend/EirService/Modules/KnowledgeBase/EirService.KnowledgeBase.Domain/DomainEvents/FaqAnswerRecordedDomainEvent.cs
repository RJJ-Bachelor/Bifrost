using EirService.KnowledgeBase.Domain.Entities;
using Shared.Domain.DomainEvents;

namespace EirService.KnowledgeBase.Domain.DomainEvents;

public sealed record FaqAnswerRecordedDomainEvent(FaqAnswer Faq) : DomainEvent(Guid.NewGuid());