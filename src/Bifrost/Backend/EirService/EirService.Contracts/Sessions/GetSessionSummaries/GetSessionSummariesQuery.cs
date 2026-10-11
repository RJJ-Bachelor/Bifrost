using EirService.Contracts.Sessions.Common;
using FastEndpoints;

namespace EirService.Contracts.Sessions.GetSessionSummaries;

public sealed record GetSessionSummariesQuery(Guid[] SessionIds) : ICommand<IReadOnlyList<StudentSessionSummary>>;
