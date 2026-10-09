namespace EirService.Api.Endpoints.Teacher.Participants;

public sealed record TeacherIdentityResponse(string UserId, string? Name, string[] Roles);
