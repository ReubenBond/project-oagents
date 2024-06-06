var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureProvisioning();

//var openai = builder.AddAzureOpenAI("openai"); // manually provisioned for now
var qdrant = builder.AddQdrant("qdrant");

var orleans = builder.AddOrleans("orleans")
    .WithDevelopmentClustering()
    .WithMemoryReminders()
    .WithMemoryGrainStorage("agent-state");

var agentHost = builder.AddProject<Projects.Support_AgentHost>("agenthost")
    //.WithReference(openai)
    .WithEnvironment("OpenAI__Endpoint", builder.Configuration["OpenAI:Endpoint"]!)
    .WithEnvironment("OpenAI__Key", builder.Configuration["OpenAI:Key"]!)
    .WithReference(qdrant)
    .WithReference(orleans);

builder.AddProject<Projects.Support_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(agentHost);

builder.Build().Run();
