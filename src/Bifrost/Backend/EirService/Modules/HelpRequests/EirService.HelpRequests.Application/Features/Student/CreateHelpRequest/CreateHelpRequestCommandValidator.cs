using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Student.CreateHelpRequest
{
    public sealed class CreateHelpRequestCommandValidator : AbstractValidator<CreateHelpRequestCommand>
    {
        public CreateHelpRequestCommandValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty()
                .WithMessage(nameof(CreateHelpRequestCommand.Id));

            RuleFor(x => x.UserId)
                .NotEmpty()
                .WithMessage(nameof(CreateHelpRequestCommand.UserId));

            When(x => x.SessionId is not null || x.Generation is not null, () =>
            {
                RuleFor(x => x.SessionId).NotNull().NotEqual(Guid.Empty);
                RuleFor(x => x.Generation).NotNull().GreaterThan(0);
            });

            RuleFor(x => x.Message)
                .NotEmpty()
                .WithMessage(nameof(CreateHelpRequestCommand.Message))
                .MaximumLength(4000);
        }
    }
}
