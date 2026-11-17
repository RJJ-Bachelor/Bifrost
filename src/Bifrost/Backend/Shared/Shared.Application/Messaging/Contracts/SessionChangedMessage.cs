namespace Shared.Application.Messaging.Contracts;

// Eir supplies the authorized audience. The user IDs are never included in the client payload.
[MessageContract("session.changed")]
public sealed record SessionChangedMessage(string[] UserIds, Guid SessionId, int Generation, string Change);
