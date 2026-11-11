namespace Backend.Performance.Tests.Reporting;

public sealed record RequirementResult(
    string Id, string Description, double LimitMs, int Measurements, int Errors,
    int OverLimit, double? MaximumMs, string Status, int Cancelled = 0);

/// <summary>The three business limits; other timings remain diagnostic observations.</summary>
internal static class PerformanceRequirements
{
    internal const string Version = "eir-registration-1s-notifications-10s-v2";
    internal const string CreateOperation = "POST /api/students/sessions/{id}/requests";
    internal const string QueueOperation = "queue.first-to-teacher";
    internal const string AnswerOperation = "teacher.answer-to-student";

    internal static RequirementResult[] Evaluate(IReadOnlyList<OperationMeasurement> operations, int expectedQueueMeasurements = 1) =>
    [
        Evaluate("K1", "Register HelpRequest: HTTP send to complete response body", 1000,
            operations.Where(o => o.Category is "http" or "cancelled" && o.Operation == CreateOperation)),
        Evaluate("K2", "First request in a fresh queue: HTTP send to teacher notification", 10000,
            operations.Where(o => o.Category is "notification" or "cancelled" && o.Operation == QueueOperation), expectedQueueMeasurements),
        Evaluate("K3", "Teacher answer: HTTP send to the student's HelpRequestAnswered notification", 10000,
            operations.Where(o => o.Category is "notification" or "cancelled" && o.Operation == AnswerOperation))
    ];

    private static RequirementResult Evaluate(string id, string description, double limit,
        IEnumerable<OperationMeasurement> observations, int minimumMeasurements = 1)
    {
        var all = observations.ToArray();
        var cancelled = all.Count(o => o.Category == "cancelled");
        var values = all.Where(o => o.Category != "cancelled").ToArray();
        var errors = values.Count(o => !o.Success);
        var overLimit = values.Count(o => o.DurationMs > limit);
        return new(id, description, limit, values.Length, errors, overLimit,
            values.Length == 0 ? null : values.Max(o => o.DurationMs),
            errors > 0 || overLimit > 0 ? "failed" : cancelled > 0 ? "incomplete" :
            values.Length == 0 ? "not-measured" : values.Length < minimumMeasurements ? "incomplete" : "passed", cancelled);
    }
}
