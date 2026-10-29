using EirService.KnowledgeBase.Application.Features.Common;
using FastEndpoints;

namespace EirService.KnowledgeBase.Application.Features.Teacher.CreateFaq;

public sealed record CreateFaqCommand(Guid SessionId, string TeacherId, string Question, string Answer) : ICommand<FaqResult>;
