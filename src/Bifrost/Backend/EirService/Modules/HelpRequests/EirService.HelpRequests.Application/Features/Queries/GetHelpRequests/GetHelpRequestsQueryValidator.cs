using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Queries.GetHelpRequests;

public sealed class GetHelpRequestsQueryValidator : AbstractValidator<GetHelpRequestsQuery>
{
    public GetHelpRequestsQueryValidator()
    {
        RuleFor(query => query.UserId).NotEmpty().WithMessage(nameof(GetHelpRequestsQuery.UserId));
    }
}
