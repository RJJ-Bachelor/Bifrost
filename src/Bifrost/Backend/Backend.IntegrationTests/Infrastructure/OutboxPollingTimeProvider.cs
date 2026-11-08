namespace Backend.IntegrationTests.Infrastructure;

/// <summary>Signals when a worker becomes idle, without waiting for a wall-clock polling interval.</summary>
internal sealed class OutboxPollingTimeProvider : TimeProvider
{
    private readonly TaskCompletionSource waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitingForPoll => waiting.Task;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        waiting.TrySetResult();
        return System.CreateTimer(callback, state, dueTime, period);
    }
}
