using EirService.HelpRequests.Application.Features.Common;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.GetQueue;

public sealed record GetQueueQuery(Guid SessionId, string TeacherId) : ICommand<TeacherQueueResult>;

public sealed record TeacherQueueResult(
    Guid SessionId, int Generation, int WaitingCount, IReadOnlyList<HelpRequestResult> Requests, IReadOnlyList<HelpRequestResult> ActiveRequests);
