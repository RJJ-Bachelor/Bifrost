using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.GetStudentQueueStatus;

public sealed record GetStudentQueueStatusQuery(Guid SessionId, string StudentId) : ICommand<StudentQueueStatusResult>;

public sealed record StudentQueueStatusResult(int WaitingCount, int? EstimatedWaitMinutes);
