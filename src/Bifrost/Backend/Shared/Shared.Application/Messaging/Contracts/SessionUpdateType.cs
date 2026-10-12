using System.Text.Json.Serialization;

namespace Shared.Application.Messaging.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<SessionUpdateType>))]
public enum SessionUpdateType
{
    Cleared = 1,
    Deleted = 2,
    Archived = 3
}
