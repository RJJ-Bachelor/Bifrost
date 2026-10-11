using FluentValidation;

namespace EirService.Sessions.Application.Features.Teacher.GetSessions;

public sealed class GetSessionsQueryValidator : AbstractValidator<GetSessionsQuery>
{
    public GetSessionsQueryValidator() => RuleFor(query => query.TeacherId).NotEmpty();
}
