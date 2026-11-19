using System.Net.WebSockets;
using System.Text;
using Shared.Testing;

namespace Shared.Test.Testing;

public sealed class SignalRTestClientTests
{
    [Fact]
    public async Task HandshakePingsAndCoalescedUnicodeNotificationsPreserveEveryRecord()
    {
        var notification = "{\"type\":1,\"arguments\":[\"" + new string('x', 10_000) + "€😀\"]}\u001e";
        const string next = "{\"type\":1,\"arguments\":[\"next\"]}\u001e";
        // Put multi-byte UTF-8 on the wire, split inside the euro character.
        var wire = Encoding.UTF8.GetBytes("{}\u001e{\"type\":6}\u001e" + notification + next);
        var split = Array.IndexOf(wire, (byte)0xe2) + 1;
        Assert.True(split > 0);
        using var socket = new ScriptedSocket(wire[..split], wire[split..]);

        await SignalRTestClient.HandshakeAsync(socket);

        Assert.Equal("{\"protocol\":\"json\",\"version\":1}\u001e", socket.Sent);
        Assert.Equal(notification, await SignalRTestClient.ReadAsync(socket));
        Assert.Equal(next, await SignalRTestClient.ReadAsync(socket));
    }

    [Fact]
    public async Task ClosingBeforeTheRecordEndsReportsTheIncompleteRead()
    {
        using var socket = new ScriptedSocket(Encoding.UTF8.GetBytes("{\"type\":1"));

        var failure = await Assert.ThrowsAsync<WebSocketException>(() => SignalRTestClient.ReadAsync(socket));

        Assert.Contains("before a complete record", failure.Message);
    }

    [Fact]
    public async Task RejectedHandshakeReportsTheServersError()
    {
        using var socket = new ScriptedSocket(Encoding.UTF8.GetBytes("{\"error\":\"Unsupported protocol\"}\u001e"));

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => SignalRTestClient.HandshakeAsync(socket));

        Assert.Contains("Unsupported protocol", failure.Message);
    }

    private sealed class ScriptedSocket(params byte[][] chunks) : WebSocket
    {
        private readonly Queue<byte[]> pending = new(chunks);
        private int offset;
        public string Sent { get; private set; } = "";
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pending.Count == 0) return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            var chunk = pending.Peek();
            var count = Math.Min(buffer.Count, chunk.Length - offset);
            chunk.AsSpan(offset, count).CopyTo(buffer.AsSpan());
            offset += count;
            var complete = offset == chunk.Length;
            if (complete) { pending.Dequeue(); offset = 0; }
            return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, complete));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent += Encoding.UTF8.GetString(buffer.AsSpan());
            return Task.CompletedTask;
        }
    }
}
