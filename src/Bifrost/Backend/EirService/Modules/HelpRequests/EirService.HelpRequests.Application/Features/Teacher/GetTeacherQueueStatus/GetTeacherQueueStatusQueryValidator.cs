using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Teacher.GetTeacherQueueStatus;

public sealed class GetTeacherQueueStatusQueryValidator : AbstractValidator<GetTeacherQueueStatusQuery>
{
    public GetTeacherQueueStatusQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.TeacherId).NotEmpty().MaximumLength(100);
    }
}
