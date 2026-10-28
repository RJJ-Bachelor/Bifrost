using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.GetTeacherQueueStatus;

public sealed record GetTeacherQueueStatusQuery(Guid SessionId, string TeacherId) : ICommand<TeacherQueueStatusResult>;

public sealed record TeacherQueueStatusResult(
    int WaitingCount, int UnreservedWaitingCount, double? FrontWaitMinutes, int Generation, Guid? MyReservedRequestId);
