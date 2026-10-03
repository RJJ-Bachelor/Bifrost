using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var keycloak = builder.AddKeycloak("keycloak", 60000)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
    .WithEndpointProxySupport(false)
    .WithEndpoint("http", endpoint =>
    {
        endpoint.TargetHost = "0.0.0.0";
        endpoint.TargetPort = 8443;
        endpoint.Port = 60000;
        endpoint.UriScheme = "https";
        endpoint.IsProxied = false;
    }, createIfNotExists: false)
    .WithDataVolume()
    .WithRealmImport("./keycloak/Bifrost-realm.json");

var eirService = builder.AddProject<Projects.EirService_Api>("eirservice-api")
    .WithReference(keycloak)
    .WithHttpEndpoint(port: 60001)
    .WaitFor(keycloak);

var mimirService = builder.AddProject<Projects.MimirService_Api>("mimirservice-api")
    .WithReference(keycloak)
    .WithHttpEndpoint(port: 60003)
    .WaitFor(keycloak);

var gnaService = builder.AddProject<Projects.GnaService_Api>("gnaservice-api")
    .WithReference(keycloak)
    .WithHttpEndpoint(port: 60002)
    .WaitFor(keycloak);

var heimdallGateway = builder.AddProject<Projects.HeimdallGateway>("heimdallgateway")
    .WithReference(keycloak)
    .WithReference(eirService)
    .WithReference(mimirService)
    .WithReference(gnaService)
    .WithHttpEndpoint(port: 60004)
    .WaitFor(keycloak)
    .WaitFor(eirService)
    .WaitFor(mimirService)
    .WaitFor(gnaService);

builder.AddJavaScriptApp("glitnir-frontend", "../../../Frontend/GlitnirFrontend")
    .WithRunScript("start")
    //.WithNpm(install: false)
    .WithHttpEndpoint(targetPort: 7002, port: 7000, env: "PORT")
    .WaitFor(heimdallGateway);

builder.AddJavaScriptApp("test-frontend", "../../../Temp/TestFrontend")
    .WithRunScript("start")
    //.WithNpm(install: false)
    .WithHttpEndpoint(targetPort: 7003, port: 7001, env: "PORT")
    .WaitFor(heimdallGateway);

builder.Build().Run();
