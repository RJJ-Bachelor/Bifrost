using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace GnaService.Infrastructure.Push;

// Reuse authentication tokens between deliveries, as required by Apple Web Push.
public sealed class VapidTokenCache(IMemoryCache cache) : IVapidTokenCache
{
    public string Get(string audience) => cache.Get<string>((nameof(VapidTokenCache), audience))!;

    public void Put(string audience, DateTimeOffset expiration, string token) =>
        cache.Set((nameof(VapidTokenCache), audience), token, expiration);
}
