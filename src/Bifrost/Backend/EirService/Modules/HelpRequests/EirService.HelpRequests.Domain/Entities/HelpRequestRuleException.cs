namespace EirService.HelpRequests.Domain.Entities;

public sealed class HelpRequestRuleException(string message) : InvalidOperationException(message);
