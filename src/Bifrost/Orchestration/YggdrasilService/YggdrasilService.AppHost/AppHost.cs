using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// RabbitMQ
var rabbitmqUsername = builder.AddParameter("rabbitmq-username", "guest");
var rabbitmqPassword = builder.AddParameter("rabbitmq-password", "guest", secret: true);

var rabbitmq = builder
    .AddRabbitMQ("rabbitmq", rabbitmqUsername, rabbitmqPassword, port: 5001)
    .WithDataVolume()
    .WithManagementPlugin(port: 5002);

// Postgres
var postgres = builder.AddPostgres("postgres", port: 5000)
    .WithDataVolume()
    .WithInitFiles("./postgres/init");

var eirDatabase = postgres.AddDatabase("eir");
var gnaDatabase = postgres.AddDatabase("gna");
var mimirDatabase = postgres.AddDatabase("mimir");
var varDatabase = postgres.AddDatabase("var");

// IdentityServer
var varService = builder.AddProject<Projects.VarService>("varservice", launchProfileName: "http")
    .WithReference(varDatabase)
    .WithHttpEndpoint(port: 6003)
    .WaitFor(postgres);


// Bifrost services
var eirService = builder.AddProject<Projects.EirService_Api>("eirservice-api")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WithReference(eirDatabase)
    .WithHttpEndpoint(port: 6000)
    .WaitFor(postgres)
    .WaitFor(rabbitmq)
    .WaitFor(postgres);

var gnaService = builder.AddProject<Projects.GnaService_Api>("gnaservice-api")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WithReference(gnaDatabase)
    .WithHttpEndpoint(port: 6001)
    .WaitFor(postgres)
    .WaitFor(rabbitmq)
    .WaitFor(postgres);

var mimirService = builder.AddProject<Projects.MimirService_Api>("mimirservice-api")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WithReference(mimirDatabase)
    .WithHttpEndpoint(port: 6002)
    .WaitFor(postgres)
    .WaitFor(rabbitmq)
    .WaitFor(postgres);

var heimdallGateway = builder.AddProject<Projects.HeimdallGateway>("heimdallgateway")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WithReference(eirDatabase)
    .WithReference(eirService)
    .WithReference(mimirService)
    .WithReference(gnaService)
    .WithHttpEndpoint(port: 6004)
    .WaitFor(postgres)
    .WaitFor(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(eirService)
    .WaitFor(mimirService)
    .WaitFor(gnaService);

// Frontend apps
builder.AddJavaScriptApp("glitnir-frontend", "../../../Frontend/GlitnirFrontend")
    .WithRunScript("start")
    .WithHttpEndpoint(targetPort: 7002, port: 7000, env: "PORT")
    .WaitFor(heimdallGateway);

builder.AddJavaScriptApp("test-frontend", "../../../Temp/TestFrontend")
    .WithRunScript("start")
    .WithHttpEndpoint(targetPort: 7003, port: 7001, env: "PORT")
    .WaitFor(heimdallGateway);


// Build
builder.Build().Run();
