var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.HeimdallGateway>("heimdallgateway");

builder.AddProject<Projects.EirService_Api>("eirservice-api");

builder.AddProject<Projects.MimirService_Api>("mimirservice-api");

builder.AddProject<Projects.GnaService_Api>("gnaservice-api");

builder.Build().Run();
