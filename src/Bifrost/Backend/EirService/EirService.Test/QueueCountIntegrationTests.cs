using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EirService.HelpRequests.Domain.Entities;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task QueueCountsTrackReservationsAndReturnsForTeachersAndStudents()
    {
        var session = await CreateAsync();
        await JoinAsync(session.Code);
        await AssertQueueCountsAsync(session.Id, waiting: 0, unreserved: 0);

        var physicalId = await SubmitAsync(student, session.Id, HelpType.Physical);
        clock.Now = clock.Now.AddMinutes(1);
        using var otherStudent = await OtherStudentAsync(session.Code);
        var digitalId = await SubmitAsync(otherStudent, session.Id, HelpType.Digital);
        await AssertQueueCountsAsync(session.Id, waiting: 2, unreserved: 2);

        using var reservation = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, reservation.StatusCode);
        Assert.Equal(physicalId, (await reservation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        await AssertQueueCountsAsync(session.Id, waiting: 2, unreserved: 1);

        using var started = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{physicalId}/status", new { Action = "Start" });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        await AssertQueueCountsAsync(session.Id, waiting: 1, unreserved: 1);

        using var returned = await teacher.PatchAsJsonAsync($"/api/teachers/requests/{physicalId}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        await AssertQueueCountsAsync(session.Id, waiting: 2, unreserved: 2);

        using var secondReservation = await teacher.PostAsync($"/api/teachers/sessions/{session.Id}/queue/take-next", null);
        Assert.Equal(HttpStatusCode.OK, secondReservation.StatusCode);
        Assert.Equal(physicalId, (await secondReservation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        await AssertQueueCountsAsync(session.Id, waiting: 2, unreserved: 1);

        using var reservedCancellation = await student.PatchAsJsonAsync($"/api/students/requests/{physicalId}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, reservedCancellation.StatusCode);
        await AssertQueueCountsAsync(session.Id, waiting: 1, unreserved: 1);

        using var waitingCancellation = await otherStudent.PatchAsJsonAsync($"/api/students/requests/{digitalId}/status", new { Action = "Cancel" });
        Assert.Equal(HttpStatusCode.OK, waitingCancellation.StatusCode);
        await AssertQueueCountsAsync(session.Id, waiting: 0, unreserved: 0);
    }

    private async Task AssertQueueCountsAsync(Guid sessionId, int waiting, int unreserved)
    {
        var teacherQueue = await teacher.GetFromJsonAsync<JsonElement>($"/api/teachers/sessions/{sessionId}/queue");
        var studentQueue = await student.GetFromJsonAsync<JsonElement>($"/api/students/sessions/{sessionId}/queue");
        var sessions = await teacher.GetFromJsonAsync<JsonElement>("/api/teachers/sessions");
        var listedSession = sessions.EnumerateArray().Single(session => session.GetProperty("id").GetGuid() == sessionId);
        foreach (var result in new[] { teacherQueue, studentQueue, listedSession })
        {
            Assert.Equal(waiting, result.GetProperty("waitingCount").GetInt32());
            Assert.Equal(unreserved, result.GetProperty("unreservedWaitingCount").GetInt32());
        }
    }
}
