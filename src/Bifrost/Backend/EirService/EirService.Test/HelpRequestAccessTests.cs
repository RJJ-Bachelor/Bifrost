using EirService.Contracts.Exceptions;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using EirService.Participants.Application.Features.Student.JoinSession;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Participants.Application.Repositories;
using EirService.Participants.Domain.Entities;
using EirService.Sessions.Application.Features.Teacher.ClearSession;
using EirService.Sessions.Application.Features.Teacher.CreateSession;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    [Fact]
    public async Task StudentAccessUsesTheCurrentGenerationWithoutReturningTheTeacherRoster()
    {
        var session = await new CreateSessionCommand("owner", "Shared queue").ExecuteAsync();
        await new JoinTeacherSessionCommand(session.TeacherCode, "colleague").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "student").ExecuteAsync();
        await new ClearSessionCommand(session.Id, "owner", 1).ExecuteAsync();
        var access = new HelpRequestAccess(_requests);

        var student = await access.ForSessionAsync(session.Id, "student", AuthorRole.Student, default);
        Assert.Equal((session.Id, 2), (student.Id, student.Generation));
        Assert.Empty(student.TeacherIds);

        var teacher = await access.ForSessionAsync(session.Id, "colleague", AuthorRole.Teacher, default);
        Assert.Equal((session.Id, 2), (teacher.Id, teacher.Generation));
        Assert.Equal(new[] { "colleague", "owner" }, teacher.TeacherIds.Order());
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("outsider")]
    public async Task StudentAccessRejectsSubjectsWithoutStudentMembership(string actorId)
    {
        var session = await new CreateSessionCommand("owner", "Shared queue").ExecuteAsync();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new HelpRequestAccess(_requests)
            .ForSessionAsync(session.Id, actorId, AuthorRole.Student, default));
    }

    [Fact]
    public async Task OrphanedStudentMembershipDoesNotGrantAccessToAMissingSession()
    {
        var missingSessionId = Guid.NewGuid();
        await _app.Services.GetRequiredService<IParticipantRepository>().JoinAsync(
            SessionParticipant.Join(missingSessionId, "student", ParticipantRole.Student, DateTimeOffset.UtcNow), default);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new HelpRequestAccess(_requests)
            .ForSessionAsync(missingSessionId, "student", AuthorRole.Student, default));
    }

    [Fact]
    public async Task StudentRequestAccessPreservesOwnershipAndCurrentGenerationRules()
    {
        var session = await new CreateSessionCommand("owner", "Shared queue").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "student").ExecuteAsync();
        await new JoinSessionCommand(session.Code, "observer").ExecuteAsync();
        var request = HelpRequest.Create(session.Id, 1, "student", HelpType.Digital,
            new MessageText("Private question"), DateTimeOffset.UtcNow);
        request.ClearDomainEvents();
        _requests.Saved.Add(request);
        await new ClearSessionCommand(session.Id, "owner", 1).ExecuteAsync();
        var access = new HelpRequestAccess(_requests);

        Assert.Same(request, await access.ForRequestAsync(request.Id, "student", AuthorRole.Student, false, default));
        await Assert.ThrowsAsync<OperationConflictException>(() =>
            access.ForRequestAsync(request.Id, "student", AuthorRole.Student, true, default));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            access.ForRequestAsync(request.Id, "observer", AuthorRole.Student, false, default));
    }
}
