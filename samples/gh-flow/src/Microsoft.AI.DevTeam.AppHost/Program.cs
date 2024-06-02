using Azure.Provisioning.CognitiveServices;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureProvisioning();
var openAi = builder.AddAzureOpenAI("openai");

var qdrant = builder.AddQdrant("qdrant");

var orleans = builder.AddOrleans("orleans")
    .WithDevelopmentClustering()
    .WithMemoryReminders()
    .WithMemoryStreaming("StreamProvider")
    .WithMemoryGrainStorage("PubSubStore")
    .WithMemoryGrainStorage("messages");

builder.AddProject<Projects.Microsoft_AI_DevTeam>("devteam")
    .WithReference(orleans)
    .WithReference(qdrant)
    .WithReference(openAi);

builder.Build().Run();
