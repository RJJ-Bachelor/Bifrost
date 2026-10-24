using FastEndpoints;

namespace EirService.Sessions.Application.Features.Teacher.ArchiveSession;

public sealed record ArchiveSessionCommand(Guid SessionId, string TeacherId, int ExpectedGeneration) : ICommand<ArchiveSessionResult>;
