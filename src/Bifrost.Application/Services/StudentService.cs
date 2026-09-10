using Bifrost.Application.DTOs;
using Bifrost.Application.Interfaces;
using Bifrost.Domain.Entities;

namespace Bifrost.Application.Services;

public class StudentService
{
    private readonly IStudentRepository _repository;

    public StudentService(IStudentRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<StudentDto> CreateStudentAsync(CreateStudentRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var student = new Student(request.FirstName, request.LastName, request.Email);
        await _repository.AddAsync(student, cancellationToken);

        return new StudentDto(student.Id, $"{student.FirstName} {student.LastName}", student.Email);
    }
}
