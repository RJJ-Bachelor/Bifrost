namespace EirService.Contracts.HelpRequests.GetWaitingCounts;

public readonly record struct WaitingCounts(int WaitingCount, int UnreservedWaitingCount);
