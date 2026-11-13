using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.Api.Endpoints.Student.Participants.GetStudentIdentity;
using EirService.HelpRequests.Domain.Entities;
using EirService.Participants.Application.Features.Student.CreateIdentityTransferCode;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shared.Api.Authentication;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    private const string IdentityTransferPath = "/api/students/identity-transfer-codes";

    [PostgresFact]
    public async Task TransferredIdentityRetainsSessionMembershipAndHelpRequestHistoryInAnotherBrowser()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        var requestId = await SubmitAsync(student, session.Id, HelpType.Digital);
        var sourceIdentity = await student.GetFromJsonAsync<StudentIdentityResponse>("/api/students/me");
        var historyBefore = await student.GetStringAsync("/api/students/requests");
        using var target = app.GetTestClient();
        using var inaccessible = await target.GetAsync($"/api/students/requests/{requestId}");
        Assert.Equal(HttpStatusCode.Unauthorized, inaccessible.StatusCode);
        var code = await CreateIdentityTransferAsync();

        using var redeemed = await target.PostAsJsonAsync(IdentityTransferPath + "/redeem", new { code.Code });

        Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);
        target.DefaultRequestHeaders.Add("Cookie", Assert.Single(redeemed.Headers.GetValues("Set-Cookie")).Split(';')[0]);
        var restoredIdentity = await target.GetFromJsonAsync<StudentIdentityResponse>("/api/students/me");
        Assert.Equal(sourceIdentity!.UserId, restoredIdentity!.UserId);
        Assert.Equal(historyBefore, await target.GetStringAsync("/api/students/requests"));
        var sessions = await target.GetFromJsonAsync<JsonElement>("/api/students/sessions");
        Assert.Equal(session.Id, Assert.Single(sessions.EnumerateArray()).GetProperty("id").GetGuid());
        using var ownRequest = await target.GetAsync($"/api/students/requests/{requestId}");
        Assert.Equal(HttpStatusCode.OK, ownRequest.StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentRedemptionsOfTheSamePersistedCodeIssueExactlyOneCookie()
    {
        var code = await CreateIdentityTransferAsync();
        using var target = app.GetTestClient();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            target.PostAsJsonAsync(IdentityTransferPath + "/redeem", new { code.Code })));

        try
        {
            var winner = Assert.Single(attempts, attempt => attempt.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(winner.Headers.GetValues("Set-Cookie"));
            Assert.All(attempts.Where(attempt => attempt != winner), attempt =>
            {
                Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
                Assert.False(attempt.Headers.Contains("Set-Cookie"));
            });
        }
        finally
        {
            foreach (var attempt in attempts) attempt.Dispose();
        }
    }

    [PostgresFact]
    public async Task ExpiredPersistedCodeDoesNotRestoreAnIdentity()
    {
        var code = await CreateIdentityTransferAsync();
        clock.Now = code.ExpiresAtUtc;
        using var target = app.GetTestClient();

        using var response = await target.PostAsJsonAsync(IdentityTransferPath + "/redeem", new { code.Code });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [PostgresFact]
    public async Task ReusingATransferCodeLeavesAnotherBrowsersExistingIdentityIntact()
    {
        var code = await CreateIdentityTransferAsync();
        using var first = app.GetTestClient();
        using var redeemed = await first.PostAsJsonAsync(IdentityTransferPath + "/redeem", new { code.Code });
        Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);
        using var other = app.GetTestClient();
        var previousId = Guid.NewGuid().ToString("N");
        var previousCookie = app.Services.GetRequiredService<StudentIdentityCookie>().Protect(previousId);
        other.DefaultRequestHeaders.Add("Cookie", $"{StudentCookieHandler.CookieName}={previousCookie}");

        using var response = await other.PostAsJsonAsync(IdentityTransferPath + "/redeem", new { code.Code });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var identity = await other.GetFromJsonAsync<StudentIdentityResponse>("/api/students/me");
        Assert.Equal(previousId, identity!.UserId);
    }

    private async Task<CreateIdentityTransferCodeResult> CreateIdentityTransferAsync()
    {
        using var response = await student.PostAsync(IdentityTransferPath, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateIdentityTransferCodeResult>())!;
    }
}
