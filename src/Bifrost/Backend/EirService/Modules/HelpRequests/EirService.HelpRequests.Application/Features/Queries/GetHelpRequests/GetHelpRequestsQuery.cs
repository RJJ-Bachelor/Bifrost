using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Queries.GetHelpRequests;

public sealed record GetHelpRequestsQuery(string UserId) : ICommand<IReadOnlyList<HelpRequestResult>>;
