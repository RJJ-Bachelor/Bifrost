using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Teacher.GetQueue;

public sealed class GetQueueQueryValidator : AbstractValidator<GetQueueQuery>
{
    public GetQueueQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.TeacherId).NotEmpty().MaximumLength(100);
    }
}
