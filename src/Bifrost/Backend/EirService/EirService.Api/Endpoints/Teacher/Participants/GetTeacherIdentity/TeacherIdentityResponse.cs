namespace EirService.Api.Endpoints.Teacher.Participants.GetTeacherIdentity;

public sealed record TeacherIdentityResponse(string UserId, string? Name, string[] Roles);
