using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Shared.Testing;

/// <summary>Reads complete SignalR records, including fragmented UTF-8 and coalesced records.</summary>
public static class SignalRTestClient
{
    private static readonly ConditionalWeakTable<WebSocket, BufferedRecords> Records = new();

    public static async Task HandshakeAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        using var deadline = Deadline(cancellationToken);
        await socket.SendAsync("{\"protocol\":\"json\",\"version\":1}\u001e"u8.ToArray(),
            WebSocketMessageType.Text, true, deadline.Token);
        var response = await ReadAsync(socket, deadline.Token);
        if (response != "{}\u001e")
            throw new InvalidDataException($"SignalR handshake failed: {response}");
    }

    public static async Task<string> ReadAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        using var deadline = Deadline(cancellationToken);
        var records = Records.GetValue(socket, _ => new BufferedRecords());
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var separator = records.Pending.IndexOf(0x1e);
            if (separator >= 0)
            {
                var bytes = records.Pending.GetRange(0, separator + 1).ToArray();
                records.Pending.RemoveRange(0, separator + 1);
                var record = Encoding.UTF8.GetString(bytes);
                using var json = JsonDocument.Parse(record.AsMemory(0, record.Length - 1));
                // Keep-alive pings may arrive while a busy test runner is scheduling work.
                if (json.RootElement.TryGetProperty("type", out var type) && type.GetInt32() == 6) continue;
                return record;
            }

            var received = await socket.ReceiveAsync(new ArraySegment<byte>(records.Buffer), deadline.Token);
            if (received.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("The SignalR connection closed before a complete record was received.");
            if (received.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("Expected a text SignalR record.");
            records.Pending.AddRange(records.Buffer.AsSpan(0, received.Count).ToArray());
        }
    }

    private static CancellationTokenSource Deadline(CancellationToken token)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        return deadline;
    }

    private sealed class BufferedRecords
    {
        public byte[] Buffer { get; } = new byte[8192];
        public List<byte> Pending { get; } = [];
    }
}
