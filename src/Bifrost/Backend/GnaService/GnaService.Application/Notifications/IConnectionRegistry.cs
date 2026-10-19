namespace GnaService.Application.Notifications;

public interface IConnectionRegistry
{
    void Add(string userId, string connectionId);
    void Remove(string connectionId);
    bool IsConnected(string userId);
}
