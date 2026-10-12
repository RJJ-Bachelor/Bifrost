using EirService.Contracts.Sessions.Common;
using FastEndpoints;

namespace EirService.Contracts.Sessions.FindSessionByCode;

public sealed record FindSessionByCodeQuery(string Code) : ICommand<StudentSessionSummary?>;
