using FluentValidation;

namespace MimirService.Application.Features.Development.GeneralizeHelpRequests;

public sealed class GeneralizeHelpRequestsCommandValidator : AbstractValidator<GeneralizeHelpRequestsCommand>
{
    public GeneralizeHelpRequestsCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.Generation).GreaterThan(0);
        RuleFor(command => command.HelpRequestIds)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(ids => ids!.Length is >= 2 and <= 100 && ids.Distinct().Count() == ids.Length)
            .WithMessage("Provide 2-100 distinct help request ids from the same session and generation.");
        RuleForEach(command => command.HelpRequestIds).NotEmpty();
        RuleFor(command => command.GeneralizationId)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("Omit the generalization id or provide a non-empty id.")
            .Must((command, id) => id is null || command.HelpRequestIds?.Contains(id.Value) != true)
            .WithMessage("The generalization cannot be one of its source help requests.");
    }
}
