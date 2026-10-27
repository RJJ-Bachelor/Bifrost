using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.GetStudentQueue;

public sealed record GetStudentQueueQuery(Guid SessionId, string StudentId) : ICommand<StudentQueueResult>;

public sealed record StudentQueueResult(
    Guid SessionId, int Generation, int WaitingCount, int UnreservedWaitingCount,
    Guid? OwnRequestId, int? Position, int? EstimatedWaitMinutes,
    int? RequestNumber, string? DisplayNumber);
