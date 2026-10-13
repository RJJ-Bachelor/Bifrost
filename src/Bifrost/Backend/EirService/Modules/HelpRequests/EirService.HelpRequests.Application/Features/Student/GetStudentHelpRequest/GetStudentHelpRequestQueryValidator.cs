using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Student.GetStudentHelpRequest;

public sealed class GetStudentHelpRequestQueryValidator : AbstractValidator<GetStudentHelpRequestQuery>
{
    public GetStudentHelpRequestQueryValidator()
    {
        RuleFor(query => query.Id).NotEmpty();
        RuleFor(query => query.StudentId).NotEmpty().MaximumLength(100);
    }
}
