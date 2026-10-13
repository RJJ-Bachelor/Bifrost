using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Teacher.TakeNextHelpRequest;

public sealed class TakeNextHelpRequestCommandValidator : AbstractValidator<TakeNextHelpRequestCommand>
{
    public TakeNextHelpRequestCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.TeacherId).NotEmpty();
    }
}
