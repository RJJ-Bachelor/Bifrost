using EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;
using EirService.HelpRequests.Domain.Entities;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using FastEndpoints;
using Shared.Api.Authentication;
using Shared.Application.Messaging.Contracts;

namespace EirService.Api.Development;

public sealed class DevelopmentDataSeeder(StudentIdentityCookie studentCookies, TimeProvider clock)
{
    public const string Scenario = "faq-demo";
    public const int MaxStudents = 20;

    private static readonly string[] Questions =
    [
        "Min for-løkke bliver ved med at køre. Hvordan får jeg den til at stoppe?",
        "Hvordan sikrer jeg, at betingelsen i min while-løkke bliver falsk?",
        "Hvad er forskellen på en klasse og et objekt?",
        "Hvordan finder jeg fejlen i min metode med debuggeren?",
        "Hvornår skal jeg bruge en liste frem for et array?",
        "Hvordan deler jeg min løsning op i mindre metoder?"
    ];

    public async Task<SeedDevelopmentDataResult> SeedAsync(string teacherId, string? displayName,
        int studentCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(studentCount, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(studentCount, MaxStudents);
        var title = $"FAQ demo - {clock.GetUtcNow():yyyy-MM-dd HH:mm:ss} UTC";
        var session = await new CreateSessionCommand(teacherId, title, displayName).ExecuteAsync(cancellationToken);
        List<SeededStudent> students = [];
        for (var index = 0; index < studentCount; index++)
        {
            var studentId = Guid.NewGuid().ToString("N");
            await new JoinSessionCommand(session.Code, studentId).ExecuteAsync(cancellationToken);
            var question = Questions[index % Questions.Length];
            var helpType = index % 2 == 0 ? HelpType.Physical : HelpType.Digital;
            var request = await new CreateHelpRequestCommand(session.Id, studentId, question, helpType).ExecuteAsync(cancellationToken);
            students.Add(new SeededStudent(studentId, request.Id, request.DisplayNumber, helpType.ToString(), question,
                $"{StudentCookieHandler.CookieName}={studentCookies.Protect(studentId)}"));
        }

        var generalization = new GeneralizedCreatedMessage(Guid.NewGuid(), session.Id, session.CurrentGeneration,
            "Hvordan sikrer jeg, at min løkke stopper?", students.Take(2).Select(student => student.HelpRequestId).ToArray());
        return new SeedDevelopmentDataResult(Scenario, session, students, generalization);
    }
}

public sealed record SeededStudent(string StudentId, Guid HelpRequestId, string DisplayNumber,
    string HelpType, string Question, string CookieHeader);

public sealed record SeedDevelopmentDataResult(string Scenario, SessionResult Session,
    IReadOnlyList<SeededStudent> Students, GeneralizedCreatedMessage Generalization);
