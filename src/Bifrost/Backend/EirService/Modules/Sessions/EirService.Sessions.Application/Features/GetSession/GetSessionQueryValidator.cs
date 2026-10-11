using FluentValidation;

namespace EirService.Sessions.Application.Features.GetSession;

public sealed class GetSessionQueryValidator : AbstractValidator<GetSessionQuery>
{
    public GetSessionQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.TeacherId).NotEmpty();
    }
}
