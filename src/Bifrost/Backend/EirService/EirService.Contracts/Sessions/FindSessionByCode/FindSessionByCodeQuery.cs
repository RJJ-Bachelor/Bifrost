using EirService.Contracts.Sessions.Common;
using FastEndpoints;

namespace EirService.Contracts.Sessions.FindSessionByCode;

public enum SessionCodeType
{
    Student,
    Teacher
}

public sealed record FindSessionByCodeQuery(string Code, SessionCodeType CodeType = SessionCodeType.Student)
    : ICommand<StudentSessionSummary?>;
