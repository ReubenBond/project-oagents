using Agents;
using Microsoft.AI.Agents.Worker.Client;
using AgentId = Microsoft.AI.Agents.Worker.Client.AgentId;
using Support.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

var agentBuilder = builder.AddAgentWorker("https://agenthost");
agentBuilder.AddAgent<GreetingAgent>("greeter");
builder.Services.AddHostedService<MyBackgroundService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();
builder.Services.AddSingleton<Client>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.UseOutputCache();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

internal sealed class GreetingAgent(IAgentContext context, ILogger<GreetingAgent> logger) : AgentBase(context)
{
    protected override ValueTask HandleEvent(string ns, string topic, Microsoft.AI.Agents.Abstractions.Event @event)
    {
        logger.LogInformation("[{Id}] Received event for ns '{Namespace}', topic '{Topic}': '{Event}'.", AgentId, ns, topic, @event);
        return base.HandleEvent(ns, topic, @event);
    }

    protected override ValueTask<RpcResponse> OnRequest(RpcRequest request)
    {
        logger.LogInformation("[{Id}] Received request: '{Request}'.", AgentId, request);
        return new(new RpcResponse() { Result = "Okay!" });
    }
}

internal sealed class MyBackgroundService(ILogger<MyBackgroundService> logger, Client client) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await client.RequestAsync(new AgentId("py_greeter", "foo"), "echo", new Dictionary<string, string> { ["message"] = "Hello, agents!" });
                logger.LogInformation("Received response: {Response}", response);
            }
            catch(Exception exception)
            {
                logger.LogError(exception, "Error invoking request.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

public sealed class Client(ILogger<Client> logger, AgentWorkerRuntime runtime) : AgentBase(new ClientContext(logger, runtime))
{
    private sealed class ClientContext(ILogger<Client> logger, AgentWorkerRuntime runtime) : IAgentContext
    {
        public AgentId AgentId { get; } = new AgentId("client", Guid.NewGuid().ToString());
        public AgentBase? AgentInstance { get; set; }
        public ILogger Logger { get; } = logger;

        public async ValueTask PublishEventAsync(Event @event)
        {
            await runtime.PublishEvent(@event);
        }

        public async ValueTask SendRequestAsync(AgentBase agent, RpcRequest request)
        {
            await runtime.SendRequest(AgentInstance!, request);
        }

        public async ValueTask SendResponseAsync(RpcRequest request, RpcResponse response)
        {
            await runtime.SendResponse(response);
        }
    }
}
