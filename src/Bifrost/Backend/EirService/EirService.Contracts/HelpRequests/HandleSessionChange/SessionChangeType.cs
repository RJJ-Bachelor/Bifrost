using System.Text.Json.Serialization;

namespace EirService.Contracts.HelpRequests.HandleSessionChange;

[JsonConverter(typeof(JsonStringEnumConverter<SessionChangeType>))]
public enum SessionChangeType
{
    Cleared = 1,
    Deleted = 2,
    Archived = 3
}
