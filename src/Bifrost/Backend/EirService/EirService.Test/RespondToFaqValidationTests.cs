using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EirService.HelpRequests.Application.Features.Student.RespondToFaq;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using FastEndpoints;
using FluentValidation;
using FullHelpRequestResult = EirService.HelpRequests.Application.Features.Common.HelpRequestResult;

namespace EirService.Test;

public sealed partial class CommandBusTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"accept\":null}")]
    public async Task MissingFaqDecisionReturnsBadRequestBeforeTheHandlerRuns(string json)
    {
        AuthenticateStudent();
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.PatchAsync($"/api/students/requests/{Guid.NewGuid()}/faq", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(result.GetProperty("errors").EnumerateObject(), error =>
            string.Equals(error.Name, nameof(RespondToFaqCommand.Accept), StringComparison.OrdinalIgnoreCase));
        Assert.Empty(_requests.Saved);
        Assert.Empty(_requests.Notifications);
        Assert.Empty(_executionOrder);
    }

    [Theory]
    [InlineData(true, HelpRequestStatus.Completed)]
    [InlineData(false, HelpRequestStatus.Pending)]
    public async Task ExplicitFaqDecisionPassesApplicationValidationAndUpdatesTheRequest(bool accept, HelpRequestStatus expected)
    {
        var studentId = AuthenticateStudent();
        var sessionId = await SeedSessionAsync(studentId);
        var now = DateTimeOffset.UtcNow;
        var request = HelpRequest.Create(sessionId, 1, studentId, HelpType.Digital, new MessageText("Question"), now);
        var faqId = Guid.NewGuid();
        request.LinkToFaq(faqId, new MessageText("Shared question"), now);
        request.SuggestFaq(faqId, "teacher-123", new MessageText("Shared answer"), now);
        request.ClearDomainEvents();
        _requests.Saved.Add(request);

        using var response = await _client.PatchAsJsonAsync($"/api/students/requests/{request.Id}/faq", new { Accept = accept });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<FullHelpRequestResult>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        Assert.Equal(expected, result!.Status);
        Assert.Equal(expected, request.Status);
        Assert.Equal(accept ? faqId : (Guid?)null, request.FaqAnswerId);
        Assert.Equal(3, request.Messages.Count);
        Assert.Equal(accept ? 3 : 1, result.Messages.Count);
        Assert.Equal(accept ? 0 : 2, request.Messages.Count(message => message.IsRejected));
    }

    [Fact]
    public async Task FaqCommandValidationAlsoAppliesOutsideHttpAndCollectsAllInvalidFields()
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            new RespondToFaqCommand(Guid.Empty, "", null).ExecuteAsync(CancellationToken.None));

        Assert.Equal(new[] { nameof(RespondToFaqCommand.Accept), nameof(RespondToFaqCommand.Id), nameof(RespondToFaqCommand.StudentId) },
            error.Errors.Select(failure => failure.PropertyName).Order(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        Assert.Empty(_requests.Saved);
        Assert.Empty(_executionOrder);
    }
}
