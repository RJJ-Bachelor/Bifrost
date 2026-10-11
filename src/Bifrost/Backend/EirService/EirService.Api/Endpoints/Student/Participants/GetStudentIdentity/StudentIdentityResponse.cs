namespace EirService.Api.Endpoints.Student.Participants.GetStudentIdentity;

public sealed record StudentIdentityResponse(string UserId, string[] Roles);
