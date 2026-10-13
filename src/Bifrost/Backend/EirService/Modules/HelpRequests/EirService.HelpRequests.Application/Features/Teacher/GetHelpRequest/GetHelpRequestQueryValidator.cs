using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Teacher.GetHelpRequest;

public sealed class GetHelpRequestQueryValidator : AbstractValidator<GetHelpRequestQuery>
{
    public GetHelpRequestQueryValidator()
    {
        RuleFor(query => query.Id).NotEmpty();
        RuleFor(query => query.TeacherId).NotEmpty().MaximumLength(100);
    }
}
