using EirService.KnowledgeBase.Application.Features.Common;
using FastEndpoints;

namespace EirService.KnowledgeBase.Application.Features.Common.GetSessionFaqs;

public sealed record GetSessionFaqsQuery(Guid SessionId, string ActorId, bool IsTeacher) : ICommand<IReadOnlyList<FaqResult>>;
