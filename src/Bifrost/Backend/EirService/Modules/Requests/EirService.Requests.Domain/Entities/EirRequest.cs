namespace EirService.Requests.Domain.Entities;

public sealed class EirRequest
{
    private EirRequest()
    {
    }

    public EirRequest(string id, string message)
    {
        Id = id;
        Message = message;
    }

    public string Id { get; private set; } = null!;

    public string Message { get; private set; } = null!;
}
