using System.Collections.Concurrent;
using GnaService.Application.Notifications;

namespace GnaService.Infrastructure.Notifications;

public sealed class ConnectionRegistry : IConnectionRegistry
{
    private readonly ConcurrentDictionary<string, string> connections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> connectionCounts = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public void Add(string userId, string connectionId)
    {
        lock (sync)
        {
            if (!connections.TryAdd(connectionId, userId)) return;
            connectionCounts.AddOrUpdate(userId, 1, static (_, count) => count + 1);
        }
    }

    public void Remove(string connectionId)
    {
        lock (sync)
        {
            if (!connections.TryRemove(connectionId, out var userId)) return;

            if (connectionCounts.AddOrUpdate(userId, 0, static (_, count) => count - 1) <= 0)
                connectionCounts.TryRemove(userId, out _);
        }
    }

    public bool IsConnected(string userId) =>
        connectionCounts.TryGetValue(userId, out var count) && count > 0;
}
