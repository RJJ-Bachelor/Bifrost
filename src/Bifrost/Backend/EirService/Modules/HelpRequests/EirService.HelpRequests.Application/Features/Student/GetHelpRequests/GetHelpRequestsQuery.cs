using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.GetHelpRequests;

public sealed record GetHelpRequestsQuery(string UserId, Guid? SessionId = null) : ICommand<IReadOnlyList<HelpRequestResult>>;
