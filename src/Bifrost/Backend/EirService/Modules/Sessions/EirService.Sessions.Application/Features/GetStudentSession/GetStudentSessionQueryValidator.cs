using FluentValidation;

namespace EirService.Sessions.Application.Features.GetStudentSession;

public sealed class GetStudentSessionQueryValidator : AbstractValidator<GetStudentSessionQuery>
{
    public GetStudentSessionQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.StudentId).NotEmpty();
    }
}
