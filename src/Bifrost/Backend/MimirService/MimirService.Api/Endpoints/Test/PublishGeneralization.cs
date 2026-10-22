using MimirService.Application.Services.Messaging;
using MimirService.Api.Endpoints.Test.GeneralizeHelpRequests;
using Shared.Application.Messaging.Contracts;

namespace MimirService.Api.Endpoints.Test;

public static class PublishGeneralization
{
    public static void MapGeneralizationTestApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.MapGeneralizeHelpRequestsTestApi();
        app.MapPost("/api/test/generalizations", async (PublishGeneralizationRequest request,
            IGeneralizedMessagePublisher publisher, CancellationToken cancellationToken) =>
        {
            var ids = request.HelpRequestIds?.Distinct().Order().ToArray() ?? [];
            var id = request.GeneralizationId ?? Guid.NewGuid();
            if (id == Guid.Empty || request.SessionId == Guid.Empty || request.Generation < 1 ||
                string.IsNullOrWhiteSpace(request.Question) || request.Question.Trim().Length > 4000 ||
                ids.Length < 2 || ids.Length > 100 || ids.Contains(Guid.Empty) || ids.Contains(id))
                return Results.BadRequest(new { message = "Provide a session, generation, question (1-4000 characters), and 2-100 distinct help request ids." });
            await publisher.PublishAsync(new GeneralizedCreatedMessage(id, request.SessionId, request.Generation,
                request.Question.Trim(), ids), cancellationToken);
            return Results.Accepted(value: new { generalizationId = id });
        }).AllowAnonymous().WithName("PublishTestGeneralization").WithTags("Test");
    }
}
