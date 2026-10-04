using FluentValidation;

namespace EirService.Requests.Application.Features.Commands.CreateRequest
{
    public class CreateRequestCommandValidator : AbstractValidator<CreateRequestCommand>
    {
        public CreateRequestCommandValidator()
        {
            RuleFor(x => x.Message)
                .NotEmpty()
                .WithMessage(nameof(CreateRequestCommand.Message));
        }
    }
}
