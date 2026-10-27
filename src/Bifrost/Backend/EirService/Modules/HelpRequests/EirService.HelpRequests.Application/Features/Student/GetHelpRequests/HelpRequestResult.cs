namespace EirService.HelpRequests.Application.Features.Student.GetHelpRequests;

// The student list keeps a compact summary; GET /requests/{id} returns the conversation.
public sealed record HelpRequestResult(Guid Id, string StudentId, string Message, Guid SessionId, int Generation,
    string Status, string HelpType, int RequestNumber, string DisplayNumber, DateTimeOffset QueuedAt,
    string? AssignedTeacherId, string? AssignedTeacherName);
