var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureProvisioning();
var qdrant = builder.AddQdrant("qdrant");

var appInsights = builder.AddAzureApplicationInsights("appinsights");

var orleans = builder.AddOrleans("orleans")
    .WithDevelopmentClustering()
    .WithMemoryReminders()
    .WithMemoryStreaming("StreamProvider")
    .WithMemoryGrainStorage("PubSubStore")
    .WithMemoryGrainStorage("messages");

var backend = builder.AddProject<Projects.Marketing>("backend")
    .WithReference(orleans)
    .WithReference(qdrant)
    .WithReference(appInsights)
    .WithEnvironment("Qdrant__Endpoint", $"{qdrant.Resource.PrimaryEndpoint.Property(EndpointProperty.Url)}")
    .WithEnvironment("Qdrant__VectorSize", "1536")
    .WithEnvironment("OpenAI__Key", builder.Configuration["OpenAiKey"]);

builder.AddNpmApp("frontend", "../frontend", "dev")
    .WithReference(backend)
    .WithHttpEndpoint(port: 3000, targetPort: 3000, isProxied: false);

builder.AddProject<Projects.GraphicDesigner>("graphicdesigner");

builder.Build().Run();
