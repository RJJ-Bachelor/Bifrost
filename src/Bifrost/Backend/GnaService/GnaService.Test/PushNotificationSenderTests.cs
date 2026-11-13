using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GnaService.Application.Repositories;
using GnaService.Infrastructure.Push;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application.Messaging.Contracts;
using Subscription = GnaService.Domain.Entities.PushSubscription;

namespace GnaService.Test;

public sealed class PushNotificationSenderTests
{
    [Fact]
    public async Task DisabledPushDoesNotQuerySubscriptionsOrSendRequests()
    {
        using var fixture = new PushFixture();
        fixture.Options.Enabled = false;
        fixture.AddSubscription("registered");

        Assert.False(await fixture.Sender.TrySendAsync(Message(), CancellationToken.None));

        Assert.Empty(fixture.Store.QueriedUsers);
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task NoSubscriptionsReturnsFalseAndOnlyQueriesTheRecipient()
    {
        using var fixture = new PushFixture();
        using var cancellation = new CancellationTokenSource();
        var message = Message();

        Assert.False(await fixture.Sender.TrySendAsync(message, cancellation.Token));

        Assert.Equal([message.UserId], fixture.Store.QueriedUsers);
        Assert.Equal(cancellation.Token, fixture.Store.Token);
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task SendsEncryptedAuthenticatedNotificationToEveryRegisteredDevice()
    {
        using var fixture = new PushFixture();
        fixture.AddSubscription("phone");
        fixture.AddSubscription("tablet");
        var message = Message() with
        {
            SessionId = Guid.NewGuid(), HelpRequestId = Guid.NewGuid(), Generation = 3,
            Change = "updated", Status = "ready"
        };

        Assert.True(await fixture.Sender.TrySendAsync(message, CancellationToken.None));

        Assert.Equal([message.UserId], fixture.Store.QueriedUsers);
        Assert.Equal(2, fixture.Requests.Count);
        Assert.Equal(["/phone", "/tablet"], fixture.Requests.Select(request => request.Endpoint.AbsolutePath));
        foreach (var request in fixture.Requests)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("aes128gcm", request.ContentEncoding);
            Assert.Equal("300", request.TimeToLive);
            Assert.Equal(message.NotificationId.ToString("N"), request.Topic);
            Assert.DoesNotContain(message.Message, Encoding.UTF8.GetString(request.Body));
            Assert.InRange(request.Body.Length, 1, 4096);
            fixture.AssertVapid(request);

            using var payload = fixture.Decrypt(request);
            var notification = payload.RootElement.GetProperty("notification");
            Assert.Equal("Bifrost", notification.GetProperty("title").GetString());
            Assert.Equal(message.Message, notification.GetProperty("body").GetString());
            Assert.Equal(message.NotificationId.ToString("N"), notification.GetProperty("tag").GetString());
            var data = notification.GetProperty("data");
            Assert.Equal(message.NotificationId, data.GetProperty("notificationId").GetGuid());
            Assert.Equal(message.SessionId, data.GetProperty("sessionId").GetGuid());
            Assert.Equal(message.HelpRequestId, data.GetProperty("helpRequestId").GetGuid());
            Assert.Equal(3, data.GetProperty("generation").GetInt32());
            Assert.Equal("updated", data.GetProperty("change").GetString());
            Assert.Equal("ready", data.GetProperty("status").GetString());
            Assert.Equal(message.EventType.ToString(), data.GetProperty("eventType").GetString());
            Assert.False(data.TryGetProperty("userId", out _));
        }
        Assert.Empty(fixture.Store.Removed);
    }

    [Fact]
    public async Task ReusesVapidTokenForTheSameAudienceAcrossSenderInstances()
    {
        using var fixture = new PushFixture();
        fixture.AddSubscription("phone");

        Assert.True(await fixture.Sender.TrySendAsync(Message(), CancellationToken.None));
        Assert.True(await fixture.CreateSender().TrySendAsync(Message(), CancellationToken.None));

        Assert.Equal(2, fixture.Requests.Count);
        fixture.AssertVapid(fixture.Requests[0]);
        Assert.Equal(fixture.Requests[0].Authorization, fixture.Requests[1].Authorization);
        Assert.NotEqual(fixture.Requests[0].Topic, fixture.Requests[1].Topic);
        Assert.NotEqual(fixture.Requests[0].Body, fixture.Requests[1].Body);
    }

    [Fact]
    public async Task ConcurrentSendersUseTheSameTokenWhenTheAudienceCacheIsCold()
    {
        using var fixture = new PushFixture();
        fixture.AddSubscription("phone");

        var delivered = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            fixture.CreateSender().TrySendAsync(Message(), CancellationToken.None))));

        Assert.All(delivered, result => Assert.True(result));
        Assert.Equal(8, fixture.Requests.Count);
        fixture.AssertVapid(fixture.Requests[0]);
        Assert.Single(fixture.Requests.Select(request => request.Authorization).Distinct());
    }

    [Fact]
    public async Task LongUnicodeMessageFitsWebPushLimitAndPreservesCompleteCharactersInPreview()
    {
        using var fixture = new PushFixture();
        fixture.AddSubscription("phone");
        var message = Message() with { Message = string.Concat(Enumerable.Repeat("\U0001F600", 5_000)) };

        Assert.True(await fixture.Sender.TrySendAsync(message, CancellationToken.None));

        var request = Assert.Single(fixture.Requests);
        Assert.InRange(request.Body.Length, 1, 4096);
        using var payload = fixture.Decrypt(request);
        var preview = payload.RootElement.GetProperty("notification").GetProperty("body").GetString();
        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F600", 200)) + "\u2026", preview);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task RemovesExpiredSubscriptionAndContinuesToTheNextDevice(HttpStatusCode status)
    {
        using var fixture = new PushFixture((request, _) => Task.FromResult(new HttpResponseMessage(
            request.RequestUri!.AbsolutePath == "/expired" ? status : HttpStatusCode.Created)));
        var expired = fixture.AddSubscription("expired");
        fixture.AddSubscription("active");
        var message = Message();

        Assert.True(await fixture.Sender.TrySendAsync(message, CancellationToken.None));

        Assert.Equal([(message.UserId, expired.Endpoint)], fixture.Store.Removed);
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task AllExpiredSubscriptionsAreRemovedWithoutClaimingDelivery()
    {
        using var fixture = new PushFixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone)));
        fixture.AddSubscription("phone");
        fixture.AddSubscription("tablet");

        Assert.False(await fixture.Sender.TrySendAsync(Message(), CancellationToken.None));

        Assert.Equal(2, fixture.Store.Removed.Count);
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task TransientFailureStillAttemptsRemainingDevicesAndThrowsEvenAfterPartialSuccess(HttpStatusCode status)
    {
        using var fixture = new PushFixture((request, _) => Task.FromResult(new HttpResponseMessage(
            request.RequestUri!.AbsolutePath == "/failed" ? status : HttpStatusCode.Created)));
        fixture.AddSubscription("failed");
        fixture.AddSubscription("active");

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            fixture.Sender.TrySendAsync(Message(), CancellationToken.None));

        Assert.Equal(status, Assert.IsType<PushServiceClientException>(Assert.Single(exception.InnerExceptions)).StatusCode);
        Assert.Equal(2, fixture.Requests.Count);
        Assert.Empty(fixture.Store.Removed);
    }

    [Fact]
    public async Task NetworkFailureStillAttemptsRemainingDevicesAndIsAvailableForRetry()
    {
        using var fixture = new PushFixture((request, _) => request.RequestUri!.AbsolutePath == "/failed"
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection reset"))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)));
        fixture.AddSubscription("failed");
        fixture.AddSubscription("active");

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            fixture.Sender.TrySendAsync(Message(), CancellationToken.None));

        Assert.IsType<HttpRequestException>(Assert.Single(exception.InnerExceptions));
        Assert.Equal(2, fixture.Requests.Count);
        Assert.Empty(fixture.Store.Removed);
    }

    [Fact]
    public async Task CancellationBeforeDeliveryDoesNotQueryOrSend()
    {
        using var fixture = new PushFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Sender.TrySendAsync(Message(), cancellation.Token));

        Assert.Empty(fixture.Store.QueriedUsers);
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task CancellationDuringDeliveryPropagatesAndStopsRemainingDevices()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new PushFixture((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        });
        fixture.AddSubscription("phone");
        fixture.AddSubscription("tablet");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Sender.TrySendAsync(Message(), cancellation.Token));

        Assert.Single(fixture.Requests);
        Assert.Empty(fixture.Store.Removed);
    }

    private static NotificationDeliveryRequestedMessage Message() =>
        new(Guid.NewGuid(), "student-42", "A new answer is ready", DateTimeOffset.UtcNow);

    private sealed class PushFixture : IDisposable
    {
        private readonly ECDiffieHellman browserKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        private readonly byte[] authSecret = RandomNumberGenerator.GetBytes(16);
        private readonly byte[] browserPublicKey;
        private readonly HttpClient httpClient;
        private readonly MemoryCache tokenCache = new(new MemoryCacheOptions());
        private readonly IVapidTokenCache vapidTokenCache;
        public SubscriptionStore Store { get; } = new();
        public List<SentRequest> Requests { get; } = [];
        public WebPushOptions Options { get; }
        public PushNotificationSender Sender { get; }

        public PushFixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null)
        {
            browserPublicKey = PublicKeyBytes(browserKey.ExportParameters(false));
            using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = signingKey.ExportParameters(true);
            Options = new WebPushOptions
            {
                Enabled = true, Subject = "mailto:push@example.test",
                PublicKey = WebEncoders.Base64UrlEncode(PublicKeyBytes(parameters)),
                PrivateKey = WebEncoders.Base64UrlEncode(parameters.D!)
            };
            httpClient = new HttpClient(new RecordingHandler(Requests, respond));
            vapidTokenCache = new VapidTokenCache(tokenCache);
            Sender = CreateSender();
        }

        public PushNotificationSender CreateSender() => new(Store, new PushServiceClient(httpClient) { AutoRetryAfter = false },
            Microsoft.Extensions.Options.Options.Create(Options), vapidTokenCache, NullLogger<PushNotificationSender>.Instance);

        public Subscription AddSubscription(string device)
        {
            var subscription = Subscription.Create("student-42", "https://push.example/" + device,
                WebEncoders.Base64UrlEncode(browserPublicKey), WebEncoders.Base64UrlEncode(authSecret));
            Store.Subscriptions.Add(subscription);
            return subscription;
        }

        public void AssertVapid(SentRequest request)
        {
            Assert.StartsWith("vapid ", request.Authorization);
            var values = request.Authorization![6..].Split(',')
                .Select(part => part.Trim().Split('=', 2)).ToDictionary(part => part[0], part => part[1]);
            Assert.Equal(Options.PublicKey, values["k"]);
            var token = values["t"].Split('.');
            Assert.Equal(3, token.Length);
            using var claims = JsonDocument.Parse(WebEncoders.Base64UrlDecode(token[1]));
            Assert.Equal(request.Endpoint.GetLeftPart(UriPartial.Authority), claims.RootElement.GetProperty("aud").GetString());
            Assert.Equal(Options.Subject, claims.RootElement.GetProperty("sub").GetString());
            var expiry = DateTimeOffset.FromUnixTimeSeconds(claims.RootElement.GetProperty("exp").GetInt64());
            Assert.InRange(expiry, DateTimeOffset.UtcNow.AddMinutes(1), DateTimeOffset.UtcNow.AddHours(12).AddMinutes(1));
            var publicKey = WebEncoders.Base64UrlDecode(Options.PublicKey);
            using var verifier = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] }
            });
            Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes(token[0] + "." + token[1]),
                WebEncoders.Base64UrlDecode(token[2]), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }

        // Decode the browser-facing RFC 8291 body independently of the sender/SDK serializer.
        public JsonDocument Decrypt(SentRequest request)
        {
            var body = request.Body;
            var salt = body[..16];
            var recordSize = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4));
            var serverKeyLength = body[20];
            Assert.Equal(65, serverKeyLength);
            var serverPublicKey = body[21..(21 + serverKeyLength)];
            var encrypted = body[(21 + serverKeyLength)..];
            Assert.True(encrypted.Length <= recordSize);
            using var serverKey = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = serverPublicKey[1..33], Y = serverPublicKey[33..65] }
            });
            var sharedSecret = browserKey.DeriveRawSecretAgreement(serverKey.PublicKey);
            var keyInfo = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(browserPublicKey).Concat(serverPublicKey).ToArray();
            var inputKey = Expand(HMACSHA256.HashData(authSecret, sharedSecret), keyInfo, 32);
            var pseudorandomKey = HMACSHA256.HashData(salt, inputKey);
            var contentKey = Expand(pseudorandomKey, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"), 16);
            var nonce = Expand(pseudorandomKey, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"), 12);
            var plaintext = new byte[encrypted.Length - 16];
            using var aes = new AesGcm(contentKey, 16);
            aes.Decrypt(nonce, encrypted.AsSpan(0, plaintext.Length), encrypted.AsSpan(plaintext.Length), plaintext);
            var delimiter = plaintext.Length - 1;
            while (delimiter >= 0 && plaintext[delimiter] == 0) delimiter--;
            Assert.True(delimiter >= 0);
            Assert.Equal(2, plaintext[delimiter]);
            return JsonDocument.Parse(plaintext.AsMemory(0, delimiter));
        }

        private static byte[] Expand(byte[] key, byte[] info, int length) =>
            HMACSHA256.HashData(key, (byte[])[.. info, 1])[..length];

        private static byte[] PublicKeyBytes(ECParameters parameters) => [4, .. parameters.Q.X!, .. parameters.Q.Y!];

        public void Dispose()
        {
            httpClient.Dispose();
            tokenCache.Dispose();
            browserKey.Dispose();
        }
    }

    private sealed record SentRequest(Uri Endpoint, HttpMethod Method, string? Authorization,
        string ContentEncoding, string TimeToLive, string Topic, byte[] Body);

    private sealed class RecordingHandler(List<SentRequest> requests,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = new SentRequest(request.RequestUri!, request.Method, request.Headers.Authorization?.ToString(),
                string.Join(",", request.Content!.Headers.ContentEncoding), request.Headers.GetValues("TTL").Single(),
                request.Headers.GetValues("Topic").Single(), await request.Content.ReadAsByteArrayAsync(cancellationToken));
            lock (requests) requests.Add(sent);
            return respond is null ? new HttpResponseMessage(HttpStatusCode.Created) : await respond(request, cancellationToken);
        }
    }

    private sealed class SubscriptionStore : IPushSubscriptionRepository
    {
        public List<Subscription> Subscriptions { get; } = [];
        public List<string> QueriedUsers { get; } = [];
        public List<(string UserId, string Endpoint)> Removed { get; } = [];
        public CancellationToken Token { get; private set; }

        public Task<IReadOnlyList<Subscription>> GetForUserAsync(string userId, CancellationToken cancellationToken)
        {
            lock (QueriedUsers) QueriedUsers.Add(userId);
            Token = cancellationToken;
            return Task.FromResult<IReadOnlyList<Subscription>>(Subscriptions);
        }

        public Task SaveAsync(Subscription subscription, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemoveAsync(string userId, string endpoint, CancellationToken cancellationToken)
        {
            Removed.Add((userId, endpoint));
            return Task.CompletedTask;
        }
    }
}
