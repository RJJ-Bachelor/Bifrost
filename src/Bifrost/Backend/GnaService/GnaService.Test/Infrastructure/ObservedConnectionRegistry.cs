using GnaService.Application.Notifications;
using GnaService.Infrastructure.Notifications;

namespace GnaService.Test.Infrastructure;

// Preserve the real registry behavior while observing each individual tab's registration.
internal sealed class ObservedConnectionRegistry : IConnectionRegistry
{
    private readonly ConnectionRegistry registry = new();
    private readonly Dictionary<string, TaskCompletionSource> expected = new(StringComparer.Ordinal);
    private readonly object sync = new();
    private TaskCompletionSource? disconnected;

    public Task ExpectConnection(string userId)
    {
        lock (sync)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            expected.Add(userId, signal);
            return signal.Task;
        }
    }

    public Task ExpectDisconnection()
    {
        lock (sync)
        {
            if (disconnected is not null) throw new InvalidOperationException("A disconnect is already being observed.");
            disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return disconnected.Task;
        }
    }

    public void Add(string userId, string connectionId)
    {
        registry.Add(userId, connectionId);
        lock (sync)
            if (expected.Remove(userId, out var signal)) signal.TrySetResult();
    }

    public void Remove(string connectionId)
    {
        registry.Remove(connectionId);
        lock (sync)
        {
            disconnected?.TrySetResult();
            disconnected = null;
        }
    }

    public bool IsConnected(string userId) => registry.IsConnected(userId);
}
