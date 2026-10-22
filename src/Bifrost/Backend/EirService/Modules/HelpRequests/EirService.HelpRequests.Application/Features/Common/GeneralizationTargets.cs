using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.Entities;

namespace EirService.HelpRequests.Application.Features.Common;

internal sealed record GeneralizationTargets(IReadOnlyList<HelpRequest> Sources, HelpRequest? ExistingFaq)
{
    public static async Task<GeneralizationTargets?> FindAsync(
        IHelpRequestRepository repository,
        Guid generalizationId,
        Guid sessionId,
        int generation,
        Guid[]? helpRequestIds,
        CancellationToken cancellationToken)
    {
        var ids = helpRequestIds?.Distinct().ToArray() ?? [];
        if (generalizationId == Guid.Empty || sessionId == Guid.Empty || generation < 1 ||
            ids.Length is < 2 or > 100 || ids.Contains(Guid.Empty) || ids.Contains(generalizationId))
            return null;

        var sources = await repository.GetByIdsAsync(ids, cancellationToken);
        if (sources.Count != ids.Length || sources.Any(request => request.IsFaq ||
            request.SessionId != sessionId || request.Generation != generation))
            return null;

        var existing = await repository.GetAsync(generalizationId, cancellationToken);
        if (existing is not null && (!existing.IsFaq || existing.SessionId != sessionId || existing.Generation != generation))
            return null;

        return new GeneralizationTargets(sources, existing);
    }
}
