using FastEndpoints;

namespace EirService.Contracts.Participants.GetSessionTeacherIds;

public sealed record GetSessionTeacherIdsQuery(Guid SessionId) : ICommand<string[]>;
