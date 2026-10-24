using FluentValidation;

namespace EirService.Sessions.Application.Features.Common.GetSessionArchive;

public sealed class GetSessionArchiveQueryValidator : AbstractValidator<GetSessionArchiveQuery>
{
    public GetSessionArchiveQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.ArchiveId).NotEmpty();
        RuleFor(query => query.ActorId).NotEmpty();
    }
}
