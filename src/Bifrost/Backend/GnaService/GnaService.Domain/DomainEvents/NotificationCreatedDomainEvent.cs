using GnaService.Domain.Entities;
using Shared.Domain.DomainEvents;

namespace GnaService.Domain.DomainEvents;

public sealed record NotificationCreatedDomainEvent(Notification Notification) : DomainEvent(Guid.NewGuid());
