using FastEndpoints;

namespace EirService.Contracts.Sessions.GetHelpRequestSession;

public sealed record GetHelpRequestSessionQuery(Guid SessionId) : ICommand<HelpRequestSession?>;
public sealed record HelpRequestSession(Guid Id, int Generation, string TeacherId);
