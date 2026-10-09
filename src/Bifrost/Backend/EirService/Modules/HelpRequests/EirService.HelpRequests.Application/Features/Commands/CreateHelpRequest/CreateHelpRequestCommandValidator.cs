using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Commands.CreateHelpRequest
{
    public sealed class CreateHelpRequestCommandValidator : AbstractValidator<CreateHelpRequestCommand>
    {
        public CreateHelpRequestCommandValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty()
                .WithMessage(nameof(CreateHelpRequestCommand.Id))
                .MaximumLength(100);

            RuleFor(x => x.UserId)
                .NotEmpty()
                .WithMessage(nameof(CreateHelpRequestCommand.UserId));

            RuleFor(x => x.Message)
                .NotEmpty()
                .WithMessage(nameof(CreateHelpRequestCommand.Message))
                .MaximumLength(4000);
        }
    }
}
