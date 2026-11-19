using System.Net;
using System.Net.Sockets;

namespace Shared.Testing;

/// <summary>Forwards a test's TCP connections and can interrupt them without affecting the real server.</summary>
public sealed class TcpFaultProxy : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly List<ForwardedConnection> connections = [];
    private readonly Uri target;
    private readonly Task accepting;
    private bool disconnected;
    private bool disposed;

    private TcpFaultProxy(Uri target)
    {
        this.target = target;
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = AcceptAsync();
    }

    public int Port { get; }

    public static Task<TcpFaultProxy> StartAsync(Uri target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (!target.IsAbsoluteUri || target.Host.Length == 0)
            throw new ArgumentException("Use an absolute server URI.", nameof(target));
        return Task.FromResult(new TcpFaultProxy(target));
    }

    public async Task DisconnectAsync()
    {
        ForwardedConnection[] active;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            disconnected = true;
            active = connections.ToArray();
        }
        foreach (var connection in active) connection.Close();
        await Task.WhenAll(active.Select(connection => connection.Completion)).WaitAsync(TimeSpan.FromSeconds(10));
    }

    public void Resume()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            disconnected = false;
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var incoming = await listener.AcceptTcpClientAsync(stopping.Token);
                lock (gate)
                {
                    if (disconnected || disposed)
                    {
                        incoming.Dispose();
                        continue;
                    }
                    var connection = new ForwardedConnection(incoming);
                    connections.Add(connection);
                    // Retain every task so teardown also observes failures from completed connections.
                    connection.Completion = Task.Run(() => ForwardAsync(connection));
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (SocketException) when (stopping.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stopping.IsCancellationRequested) { }
    }

    private async Task ForwardAsync(ForwardedConnection connection)
    {
        try
        {
            var port = target.Port > 0 ? target.Port : target.Scheme == "amqps" ? 5671 : 5672;
            await connection.Outgoing.ConnectAsync(target.Host, port, stopping.Token);
            connection.Incoming.NoDelay = connection.Outgoing.NoDelay = true;
            var incoming = connection.Incoming.GetStream();
            var outgoing = connection.Outgoing.GetStream();
            var upload = incoming.CopyToAsync(outgoing, stopping.Token);
            var download = outgoing.CopyToAsync(incoming, stopping.Token);
            try { await Task.WhenAny(upload, download); }
            finally { connection.Close(); }
            await Task.WhenAll(upload, download);
        }
        catch (IOException) { }
        catch (SocketException) { }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
        finally { connection.Close(); }
    }

    public async ValueTask DisposeAsync()
    {
        ForwardedConnection[] active;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            disconnected = true;
            active = connections.ToArray();
        }
        stopping.Cancel();
        listener.Stop();
        foreach (var connection in active) connection.Close();
        try
        {
            await Task.WhenAll(active.Select(connection => connection.Completion).Append(accepting))
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { stopping.Dispose(); }
    }

    private sealed class ForwardedConnection(TcpClient incoming)
    {
        public TcpClient Incoming { get; } = incoming;
        public TcpClient Outgoing { get; } = new();
        public Task Completion { get; set; } = Task.CompletedTask;

        public void Close()
        {
            Incoming.Dispose();
            Outgoing.Dispose();
        }
    }
}
