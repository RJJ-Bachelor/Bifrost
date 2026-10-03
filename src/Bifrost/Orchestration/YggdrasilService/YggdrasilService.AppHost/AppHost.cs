using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
    .WithEndpointProxySupport(false)
    .WithEndpoint("http", endpoint =>
    {
        endpoint.TargetHost = "0.0.0.0";
        endpoint.TargetPort = 8443;
        endpoint.Port = 8080;
        endpoint.UriScheme = "https";
        endpoint.IsProxied = false;
    }, createIfNotExists: false)
    .WithDataVolume()
    .WithRealmImport("./keycloak/Bifrost-realm.json");

builder.AddProject<Projects.HeimdallGateway>("heimdallgateway")
    .WithReference(keycloak)
    .WaitFor(keycloak);

builder.AddProject<Projects.EirService_Api>("eirservice-api")
    .WithReference(keycloak)
    .WaitFor(keycloak);

builder.AddProject<Projects.MimirService_Api>("mimirservice-api")
    .WithReference(keycloak)
    .WaitFor(keycloak);

builder.AddProject<Projects.GnaService_Api>("gnaservice-api")
    .WithReference(keycloak)
    .WaitFor(keycloak);

builder.Build().Run();
