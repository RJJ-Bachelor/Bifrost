using FastEndpoints;

namespace EirService.Contracts.KnowledgeBase.RegisterGeneralization;

public sealed record RegisterGeneralizationCommand(
    Guid Id, Guid SessionId, int Generation, string Question, Guid[] HelpRequestIds) : ICommand<bool>;
