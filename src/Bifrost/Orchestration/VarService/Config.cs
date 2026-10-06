using Duende.IdentityModel;
using Duende.IdentityServer.Models;

namespace VarService
{
    public static class Config
    {
        public static IEnumerable<IdentityResource> IdentityResources =>
            new IdentityResource[]
            {
                new IdentityResources.OpenId(),
                new IdentityResources.Profile(),
                new IdentityResource("roles", "Roles", new[] { "role" }),
            };

        public static IEnumerable<ApiScope> ApiScopes =>
            new ApiScope[]
            {
                new ApiScope("scope1", "Eir API", new[] { JwtClaimTypes.Name, JwtClaimTypes.Role }),
                new ApiScope("scope2"),
            };

        public static IEnumerable<Client> Clients =>
            new Client[]
            {
                // m2m client credentials flow client
                new Client
                {
                    ClientId = "m2m.client",
                    ClientName = "Client Credentials Client",

                    AllowedGrantTypes = GrantTypes.ClientCredentials,
                    ClientSecrets = { new Secret("511536EF-F270-4058-80CA-1C89C192F69A".Sha256()) },

                    AllowedScopes = { "scope1" }
                },

                // interactive client using code flow + pkce
                new Client
                {
                    ClientId = "interactive",
                    ClientSecrets = { new Secret("49C1A7E1-0C79-4A89-A3D6-A37998FB86B0".Sha256()) },

                    AllowedGrantTypes = GrantTypes.Code,

                    RedirectUris =
                    {
                        "https://localhost:44300/signin-oidc",
                        "http://localhost:6004/signin-oidc"
                    },
                    FrontChannelLogoutUri = "http://localhost:6004/signout-oidc",
                    PostLogoutRedirectUris = { "http://localhost:6004/signout-callback-oidc" },

                    AllowOfflineAccess = true,
                    AllowedScopes = { "openid", "profile", "roles", "scope1", "scope2" }
                },
            };
    }
}
