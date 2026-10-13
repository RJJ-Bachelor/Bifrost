using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Student.GetStudentQueue;

public sealed class GetStudentQueueQueryValidator : AbstractValidator<GetStudentQueueQuery>
{
    public GetStudentQueueQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.StudentId).NotEmpty().MaximumLength(100);
    }
}
