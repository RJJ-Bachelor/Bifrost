using FastEndpoints;

namespace EirService.Sessions.Application.Features.ClearSession;

public sealed record ClearSessionCommand(Guid SessionId, string TeacherId, int ExpectedGeneration) : ICommand<ClearSessionResult>;
