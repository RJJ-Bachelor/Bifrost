using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Shared.Testing;

/// <summary>Owns a private RabbitMQ virtual host. Dispose application connections before this fixture.</summary>
public sealed class RabbitMqTestBroker : IAsyncDisposable
{
    private readonly HttpClient management;
    private readonly string virtualHost;
    private int disposed;

    private RabbitMqTestBroker(HttpClient management, string virtualHost, string connectionString)
    {
        this.management = management;
        this.virtualHost = virtualHost;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<RabbitMqTestBroker> CreateAsync(
        string amqpConnectionString, string managementBaseAddress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var amqp = new Uri(amqpConnectionString);
        if (amqp.Scheme is not ("amqp" or "amqps"))
            throw new ArgumentException("Use an AMQP connection string.", nameof(amqpConnectionString));
        var address = new Uri(managementBaseAddress.TrimEnd('/') + "/");
        if (address.Scheme is not ("http" or "https"))
            throw new ArgumentException("Use an HTTP management API address.", nameof(managementBaseAddress));

        var credentials = amqp.UserInfo.Split(':', 2);
        var username = credentials[0].Length == 0 ? "guest" : Uri.UnescapeDataString(credentials[0]);
        var password = credentials.Length < 2 ? "guest" : Uri.UnescapeDataString(credentials[1]);
        var client = new HttpClient { BaseAddress = address, Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        var virtualHost = $"bifrost_test_{Guid.NewGuid():N}";
        var connection = new UriBuilder(amqp) { Path = virtualHost }.Uri.AbsoluteUri;
        var broker = new RabbitMqTestBroker(client, virtualHost, connection);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await broker.PutAsync($"api/vhosts/{virtualHost}", "{}", deadline.Token);
            await broker.PutAsync($"api/permissions/{virtualHost}/{Uri.EscapeDataString(username)}",
                """{"configure":".*","write":".*","read":".*"}""", deadline.Token);
            return broker;
        }
        catch (Exception creationError)
        {
            // The server may have created the vhost immediately before a request timed out.
            try { await broker.DisposeAsync(); }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Could not create and clean up the isolated RabbitMQ virtual host.",
                    creationError, cleanupError);
            }
            throw;
        }
    }

    private async Task PutAsync(string path, string body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await management.PutAsync(path, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await management.DeleteAsync($"api/vhosts/{virtualHost}", deadline.Token);
            if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
        }
        finally { management.Dispose(); }
    }
}
