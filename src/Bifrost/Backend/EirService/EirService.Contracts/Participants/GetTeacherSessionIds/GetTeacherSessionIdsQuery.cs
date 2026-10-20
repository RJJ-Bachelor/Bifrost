using FastEndpoints;

namespace EirService.Contracts.Participants.GetTeacherSessionIds;

public sealed record GetTeacherSessionIdsQuery(string TeacherId) : ICommand<Guid[]>;
