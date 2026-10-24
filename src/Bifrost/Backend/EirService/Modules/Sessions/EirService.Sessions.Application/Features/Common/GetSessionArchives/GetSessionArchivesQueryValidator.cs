using FluentValidation;

namespace EirService.Sessions.Application.Features.Common.GetSessionArchives;

public sealed class GetSessionArchivesQueryValidator : AbstractValidator<GetSessionArchivesQuery>
{
    public GetSessionArchivesQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.ActorId).NotEmpty();
    }
}
