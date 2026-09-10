namespace Bifrost.Application.DTOs;

public record StudentDto(Guid Id, string FullName, string Email);

public record CreateStudentRequest(string FirstName, string LastName, string Email);
