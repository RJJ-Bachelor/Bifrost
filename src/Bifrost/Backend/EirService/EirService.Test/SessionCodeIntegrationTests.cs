using System.Net;
using System.Net.Http.Json;
using EirService.Contracts.Sessions.FindSessionByCode;
using EirService.Participants.Infrastructure.Persistence;
using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;
using EirService.Sessions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task JoinCodesAreRoleSpecificAndStudentResponsesNeverRevealTeacherCode()
    {
        var session = await CreateAsync();
        using var colleague = TeacherClient("colleague");
        using var wrongTeacher = await colleague.PostAsJsonAsync("/api/teachers/sessions/join", new { session.Code });
        using var wrongStudent = await student.PostAsJsonAsync("/api/students/sessions/join", new { Code = session.TeacherCode });
        Assert.Equal(HttpStatusCode.NotFound, wrongTeacher.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongStudent.StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>().Participants.ToArrayAsync());

        using var teacherJoin = await colleague.PostAsJsonAsync("/api/teachers/sessions/join",
            new { Code = " " + session.TeacherCode.ToLowerInvariant() + " " });
        Assert.Equal(HttpStatusCode.OK, teacherJoin.StatusCode);
        Assert.Equal(session, await teacherJoin.Content.ReadFromJsonAsync<SessionResult>());
        using var studentJoin = await student.PostAsJsonAsync("/api/students/sessions/join", new { session.Code });
        Assert.Equal(HttpStatusCode.OK, studentJoin.StatusCode);
        var studentBodies = new[]
        {
            await studentJoin.Content.ReadAsStringAsync(),
            await student.GetStringAsync($"/api/students/sessions/{session.Id}"),
            await student.GetStringAsync("/api/students/sessions")
        };
        Assert.All(studentBodies, body =>
        {
            Assert.DoesNotContain("teacherCode", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(session.TeacherCode, body, StringComparison.OrdinalIgnoreCase);
        });
    }

    [PostgresFact]
    public async Task TeacherCodeMigrationPreservesStudentCodesAndBackfillsDistinctValidTeacherCodes()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        var migrator = db.GetService<IMigrator>();
        const string previousMigration = "20261012075452_UseGuidMessageIdsAndSessionUpdated";
        await migrator.MigrateAsync(previousMigration);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO sessions.sessions ("Id", "Title", "Code", "CreatedByTeacherId", "CreatedAtUtc", "CurrentGeneration")
            VALUES ({firstId}, 'First', 'ABCD2345', 'owner', {clock.Now}, 1),
                   ({secondId}, 'Second', 'EFGH6789', 'owner', {clock.Now}, 1);
            """);
        await db.Database.MigrateAsync();
        var sessions = await db.Sessions.AsNoTracking().OrderBy(session => session.Title).ToArrayAsync();
        Assert.Equal(new[] { "ABCD2345", "EFGH6789" }, sessions.Select(session => session.Code.Value));
        Assert.Equal(2, sessions.Select(session => session.TeacherCode.Value).Distinct().Count());
        Assert.All(sessions, session =>
        {
            Assert.True(SessionCode.IsValid(session.TeacherCode.Value));
            Assert.NotEqual(session.Code, session.TeacherCode);
        });
        var savedTeacherCodes = sessions.Select(session => session.TeacherCode.Value).ToArray();
        await db.Database.MigrateAsync();
        Assert.Equal(savedTeacherCodes, await db.Sessions.AsNoTracking().OrderBy(session => session.Title)
            .Select(session => session.TeacherCode.Value).ToArrayAsync());
        var repository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        Assert.Null(await repository.GetByCodeAsync("ABCD2345", SessionCodeType.Teacher, default));
        Assert.Equal(firstId, (await repository.GetByCodeAsync(savedTeacherCodes[0], SessionCodeType.Teacher, default))!.Id);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task TeacherCodeCollisionCannotCreateASecondSession()
    {
        var original = await CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        SessionCode studentCode;
        do { studentCode = SessionCode.Generate(); }
        while (studentCode.Value == original.Code || studentCode.Value == original.TeacherCode);
        var collision = Session.Create("Collision", studentCode, new SessionCode(original.TeacherCode), "teacher", null, clock.Now);
        Assert.False(await repository.TryAddAsync(collision, default));
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        Assert.Equal(original.Id, (await db.Sessions.SingleAsync()).Id);
    }
}
