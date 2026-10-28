using FastEndpoints;

namespace EirService.Contracts.HelpRequests.GetCompletedRequestStatistics;

public sealed record GetCompletedRequestStatisticsQuery(Guid SessionId, int Generation)
    : ICommand<CompletedRequestStatistics>;

public sealed record CompletedRequestStatistics(
    int PhysicalCount, int DigitalCount, int HelpedStudentCount, double? AverageWaitMinutes);
