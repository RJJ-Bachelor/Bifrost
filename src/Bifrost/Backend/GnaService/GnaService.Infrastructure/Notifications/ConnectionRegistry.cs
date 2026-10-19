using System.Collections.Concurrent;
using GnaService.Application.Notifications;

namespace GnaService.Infrastructure.Notifications;

public sealed class ConnectionRegistry : IConnectionRegistry
{
    private readonly ConcurrentDictionary<string, string> connections = new(StringComparer.Ordinal);

    public void Add(string userId, string connectionId) => connections[connectionId] = userId;
    public void Remove(string connectionId) => connections.TryRemove(connectionId, out _);
    public bool IsConnected(string userId) => connections.Values.Contains(userId, StringComparer.Ordinal);
}
