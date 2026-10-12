using FastEndpoints;

namespace EirService.Sessions.Application.Features.Teacher.ClearSession;

public sealed record ClearSessionCommand(Guid SessionId, string TeacherId, int ExpectedGeneration) : ICommand<ClearSessionResult>;
