using FluentValidation;
using MimirService.Application.Features.Development.GeneralizeHelpRequests;

namespace MimirService.Api.Endpoints.Test.GeneralizeHelpRequests;

internal static class GeneralizeHelpRequests
{
    public static void MapGeneralizeHelpRequestsTestApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.MapPost("/api/test/generalizations/from-help-requests", async (GeneralizeHelpRequestsRequest request,
            GeneralizeHelpRequestsCommandHandler handler, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await handler.ExecuteAsync(new GeneralizeHelpRequestsCommand(
                    request.SessionId, request.Generation, request.HelpRequestIds, request.GeneralizationId), cancellationToken);
                return Results.Accepted(value: result);
            }
            catch (ValidationException exception)
            {
                return Results.ValidationProblem(exception.Errors.GroupBy(error => error.PropertyName)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()));
            }
        }).AllowAnonymous().WithName("GeneralizeTestHelpRequests").WithTags("Test");
    }
}
