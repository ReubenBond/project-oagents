var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureProvisioning();

var openai = builder.AddAzureOpenAI("openai");
var qdrant = builder.AddQdrant("qdrant");

var orleans = builder.AddOrleans("orleans")
    .WithDevelopmentClustering()
    .WithMemoryReminders()
    .WithMemoryGrainStorage("agent-state");

var agentHost = builder.AddProject<Projects.Support_AgentHost>("agenthost")
    .WithReference(openai)
    .WithReference(qdrant)
    .WithReference(orleans);

builder.AddProject<Projects.Support_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(agentHost);

builder.Build().Run();
