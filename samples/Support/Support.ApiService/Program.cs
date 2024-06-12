using Microsoft.AI.Agents.Worker;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddGrpc(options =>
{
});
builder.AddAzureOpenAIClient("openai", settings =>
{
    settings.Endpoint = new Uri(builder.Configuration["OpenAI:Endpoint"]!);
    settings.Key = builder.Configuration["OpenAI:Key"]!;
});
builder.AddAgentService();

var app = builder.Build();

app.MapAgentService();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.Run();
