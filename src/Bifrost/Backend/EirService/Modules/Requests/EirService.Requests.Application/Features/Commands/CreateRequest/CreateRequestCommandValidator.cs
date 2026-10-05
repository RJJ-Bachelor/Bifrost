using FluentValidation;

namespace EirService.Requests.Application.Features.Commands.CreateRequest
{
    public class CreateRequestCommandValidator : AbstractValidator<CreateRequestCommand>
    {
        public CreateRequestCommandValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty()
                .WithMessage(nameof(CreateRequestCommand.Id));

            RuleFor(x => x.UserId)
                .NotEmpty()
                .WithMessage(nameof(CreateRequestCommand.UserId));

            RuleFor(x => x.Message)
                .NotEmpty()
                .WithMessage(nameof(CreateRequestCommand.Message));
        }
    }
}
