using EirService.Contracts.Exceptions;
using EirService.Contracts.Participants.IsSessionParticipant;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.KnowledgeBase.Application.Features.Common;
using EirService.KnowledgeBase.Application.Repositories;
using FastEndpoints;

namespace EirService.KnowledgeBase.Application.Features.Common.GetSessionFaqs;

public sealed class GetSessionFaqsQueryHandler(IFaqRepository repository) : ICommandHandler<GetSessionFaqsQuery, IReadOnlyList<FaqResult>>
{
    public async Task<IReadOnlyList<FaqResult>> ExecuteAsync(GetSessionFaqsQuery query, CancellationToken cancellationToken)
    {
        var session = await new GetHelpRequestSessionQuery(query.SessionId).ExecuteAsync(cancellationToken)
            ?? throw new ResourceNotFoundException();
        var allowed = query.IsTeacher ? session.TeacherIds.Contains(query.ActorId, StringComparer.Ordinal)
            : await new IsSessionParticipantQuery(query.SessionId, query.ActorId).ExecuteAsync(cancellationToken);
        if (!allowed) throw new ResourceNotFoundException();
        return (await repository.GetForSessionAsync(session.Id, session.Generation, cancellationToken))
            .Where(faq => query.IsTeacher || faq.Answer is not null).Select(faq => faq.ToResult()).ToArray();
    }
}
