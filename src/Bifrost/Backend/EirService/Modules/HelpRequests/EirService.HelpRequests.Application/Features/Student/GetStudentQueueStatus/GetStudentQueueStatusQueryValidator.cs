using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Student.GetStudentQueueStatus;

public sealed class GetStudentQueueStatusQueryValidator : AbstractValidator<GetStudentQueueStatusQuery>
{
    public GetStudentQueueStatusQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.StudentId).NotEmpty().MaximumLength(100);
    }
}
