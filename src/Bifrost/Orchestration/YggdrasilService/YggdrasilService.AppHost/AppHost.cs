using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

#pragma warning disable ASPIREPERSISTENCE001
var keycloak = builder.AddKeycloak("keycloak", 5000)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
    .WithEndpointProxySupport(false)
    .WithEndpoint("http", endpoint =>
    {
        endpoint.TargetHost = "0.0.0.0";
        endpoint.TargetPort = 8443;
        endpoint.Port = 5000;
        endpoint.UriScheme = "https";
        endpoint.IsProxied = false;
    }, createIfNotExists: false)
    .WithPersistentLifetime()
    .WithDataVolume()
    .WithRealmImport("./keycloak/Bifrost-realm.json");


var rabbitmqUsername = builder.AddParameter("rabbitmq-username", "guest");
var rabbitmqPassword = builder.AddParameter("rabbitmq-password", "guest", secret: true);

var rabbitmq = builder.AddRabbitMQ("rabbitmq", rabbitmqUsername, rabbitmqPassword, port: 5002)
    .WithPersistentLifetime()
    .WithDataVolume()
    .WithManagementPlugin(port: 5003);

var postgres = builder.AddPostgres("postgres", port: 5001)
    .WithPersistentLifetime()
    .WithDataVolume()
    .WithInitFiles("./postgres/init");
#pragma warning restore ASPIREPERSISTENCE001

var eirDatabase = postgres.AddDatabase("eir");
var gnaDatabase = postgres.AddDatabase("gna");
var mimirDatabase = postgres.AddDatabase("mimir");

var eirService = builder.AddProject<Projects.EirService_Api>("eirservice-api")
    .WithReference(keycloak)
    .WithReference(rabbitmq)
    .WithReference(eirDatabase)
    .WithHttpEndpoint(port: 6000)
    .WaitFor(keycloak)
    .WaitFor(rabbitmq)
    .WaitFor(postgres);

var mimirService = builder.AddProject<Projects.MimirService_Api>("mimirservice-api")
    .WithReference(keycloak)
    .WithReference(rabbitmq)
    .WithReference(mimirDatabase)
    .WithHttpEndpoint(port: 6002)
    .WaitFor(keycloak)
    .WaitFor(rabbitmq)
    .WaitFor(postgres);

var gnaService = builder.AddProject<Projects.GnaService_Api>("gnaservice-api")
    .WithReference(keycloak)
    .WithReference(rabbitmq)
    .WithReference(gnaDatabase)
    .WithHttpEndpoint(port: 6001)
    .WaitFor(keycloak)
    .WaitFor(rabbitmq)
    .WaitFor(postgres);

var heimdallGateway = builder.AddProject<Projects.HeimdallGateway>("heimdallgateway")
    .WithReference(keycloak)
    .WithReference(rabbitmq)
    .WithReference(eirDatabase)
    .WithReference(eirService)
    .WithReference(mimirService)
    .WithReference(gnaService)
    .WithHttpEndpoint(port: 5004)
    .WaitFor(keycloak)
    .WaitFor(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(eirService)
    .WaitFor(mimirService)
    .WaitFor(gnaService);

builder.AddJavaScriptApp("glitnir-frontend", "../../../Frontend/GlitnirFrontend")
    .WithRunScript("start")
    .WithHttpEndpoint(targetPort: 7002, port: 7000, env: "PORT")
    .WaitFor(heimdallGateway);

builder.AddJavaScriptApp("test-frontend", "../../../Temp/TestFrontend")
    .WithRunScript("start")
    .WithHttpEndpoint(targetPort: 7003, port: 7001, env: "PORT")
    .WaitFor(heimdallGateway);

builder.Build().Run();
