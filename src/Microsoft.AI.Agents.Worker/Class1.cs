using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Orleans.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Agents;
using Orleans.Streams;
using Event = Microsoft.AI.Agents.Abstractions.Event;
using RpcEvent = Agents.Event;
using System.Text.Json;
using Orleans.Serialization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using OrleansCodeGen.Orleans.Runtime;

namespace Microsoft.AI.Agents.Worker;

public static class OrleansWorkerServerHostingExtensions
{
    public static IHostApplicationBuilder AddAgents(this IHostApplicationBuilder builder)
    {
        builder.Services.AddGrpc();
        builder.Services.AddSerializer(serializer => serializer.AddProtobufSerializer());

        // Ensure Orleans is added before the hosted service to guarantee that it starts first.
        builder.Services.AddOrleans(_ => { });
        builder.Services.AddSingleton<WorkerGateway>();
        builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<WorkerGateway>());

        return builder;
    }

    public static WebApplication MapAgents(this WebApplication app, PathString path)
    {
        app.MapGrpcService<OrleansRpcService>();
        return app;
    }
}

internal class OrleansRpcService(WorkerGateway agentWorker, IClusterClient orleansClient) : AgentRpc.AgentRpcBase 
{
    public override async Task OpenChannel(IAsyncStreamReader<Message> requestStream, IServerStreamWriter<Message> responseStream, ServerCallContext context)
    {
        await agentWorker.ConnectToWorkerProcess(requestStream, responseStream, context);
    }
}

public interface IWorkerAgentRegistryGrain : IGrainWithIntegerKey
{
    ValueTask RegisterAgentType(string type, IWorkerGateway worker);
    ValueTask UnregisterAgentType(string type, IWorkerGateway worker);
    ValueTask AddWorker(IWorkerGateway worker);
    ValueTask RemoveWorker(IWorkerGateway worker);
    ValueTask<IWorkerGateway?> GetCompatibleWorker(string type);
    ValueTask<IWorkerGateway?> GetOrPlaceAgent(AgentId agentId);
}

internal sealed class WorkerState
{
    public HashSet<string> SupportedTypes { get; set; } = [];
    public DateTimeOffset LastSeen { get; set; }
}

public sealed class AgentRegistryGrain : Grain, IWorkerAgentRegistryGrain
{
    // TODO: use persistent state for some of these or (better) extend Orleans to implement some of this natively.
    private readonly Dictionary<IWorkerGateway, WorkerState> _workerStates = [];
    private readonly Dictionary<string, List<IWorkerGateway>> _supportedAgentTypes = [];
    private readonly Dictionary<(string Type, string Key), IWorkerGateway> _agentDirectory = [];
    private readonly TimeSpan _agentTimeout = TimeSpan.FromMinutes(1);

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        RegisterTimer(static state => ((AgentRegistryGrain)state)!.PurgeInactiveWorkers(), this, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        return base.OnActivateAsync(cancellationToken);
    }

    private Task PurgeInactiveWorkers()
    {
        foreach (var (worker, state) in _workerStates)
        {
            if (DateTimeOffset.UtcNow - state.LastSeen > _agentTimeout)
            {
                _workerStates.Remove(worker);
                foreach (var type in state.SupportedTypes)
                {
                    if (_supportedAgentTypes.TryGetValue(type, out var workers))
                    {
                        workers.Remove(worker);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    public ValueTask AddWorker(IWorkerGateway worker)
    {
        var workerStates = _workerStates[worker] ??= new();
        workerStates.LastSeen = DateTimeOffset.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask RegisterAgentType(string type, IWorkerGateway worker)
    {
        var supportedAgentTypes = _supportedAgentTypes[type] ??= [];
        if (!supportedAgentTypes.Contains(worker))
        {
            supportedAgentTypes.Add(worker);
        }

        var workerStates = _workerStates[worker] ??= new();
        workerStates.SupportedTypes.Add(type);
        workerStates.LastSeen = DateTimeOffset.UtcNow;

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveWorker(IWorkerGateway worker)
    {
        if (_workerStates.Remove(worker, out var state))
        {
            foreach (var type in state.SupportedTypes)
            {
                if (_supportedAgentTypes.TryGetValue(type, out var workers))
                {
                    workers.Remove(worker);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UnregisterAgentType(string type, IWorkerGateway worker)
    {
        if (_workerStates.TryGetValue(worker, out var state))
        {
            state.SupportedTypes.Remove(type);
        }

        if (_supportedAgentTypes.TryGetValue(type, out var workers))
        {
            workers.Remove(worker);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IWorkerGateway?> GetCompatibleWorker(string type) => new(GetCompatibleWorkerCore(type));

    private IWorkerGateway? GetCompatibleWorkerCore(string type)
    {
        if (_supportedAgentTypes.TryGetValue(type, out var workers))
        {
            // Return a random compatible worker.
            return workers[Random.Shared.Next(workers.Count)];
        }

        return null;
    }

    public ValueTask<IWorkerGateway?> GetOrPlaceAgent(AgentId agentId)
    {
        if (!_agentDirectory.TryGetValue((agentId.Type, agentId.Key), out var worker) || !_workerStates.ContainsKey(worker))
        {
            worker = GetCompatibleWorkerCore(agentId.Type);
            if (worker is not null)
            {
                _agentDirectory[(agentId.Type, agentId.Key)] = worker;
            }
        }

        return new(worker);
    }
}

public interface IWorkerGateway : IGrainObserver
{
    ValueTask<RpcResponse> InvokeRequest(RpcRequest request);
    ValueTask BroadcastEvent(RpcEvent evt);
}

internal sealed class WorkerGateway : BackgroundService, IWorkerGateway
{
    // The local mapping of agents to worker processes.
    private readonly ConcurrentDictionary<WorkerProcessConnection, WorkerProcessConnection> _workers = new();
    private readonly ConcurrentDictionary<(string Type, string Key), WorkerProcessConnection> _agentDirectory = new();
    private readonly ConcurrentDictionary<string, IWorkerGateway> _agentTypeDirectory = new();
    private readonly ILogger<WorkerGateway> _logger;

    public WorkerGateway(IClusterClient clusterClient, ILogger<WorkerGateway> logger)
    {
        _logger = logger;
        this.ClusterClient = clusterClient;
        this.Reference = clusterClient.CreateObjectReference<IWorkerGateway>(this);
    }

    public IClusterClient ClusterClient { get; }
    public IWorkerGateway Reference { get; }

    public async ValueTask BroadcastEvent(RpcEvent evt)
    {
        var tasks = new List<Task>(_agentDirectory.Count);
        foreach (var (_, connection) in _agentDirectory)
        {
            tasks.Add(connection.SendMessage(new Message { Event = evt }));
        }

        await Task.WhenAll(tasks);
    }

    public ValueTask<RpcResponse> InvokeRequest(RpcRequest request)
    {
        if (!_agentDirectory.TryGetValue((request.Target.Type, request.Target.Key), out var connection))
        {
            // Activate the agent on a compatible worker process.

            return new(new RpcResponse { Error = "Agent not found." });
        }

        // Proxy the request to the agent.
        return new(request));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var registry = ClusterClient.GetGrain<IWorkerAgentRegistryGrain>(0);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await registry.AddWorker(Reference);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Error adding worker to registry.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15));
        }

        try
        {
            await registry.RemoveWorker(Reference);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Error removing worker from registry.");
        }
    }

    internal async Task OnReceivedMessageAsync(WorkerProcessConnection connection, Message message)
    {
        switch (message.MessageCase)
        {
            case Message.MessageOneofCase.Request:
                await DispatchRequestAsync(connection, message.Request);
                break;
            case Message.MessageOneofCase.Response:
                DispatchResponse(connection, message.Response);
                break;
            case Message.MessageOneofCase.Event:
                await DispatchEventAsync(connection, message.Event);
                break;
            case Message.MessageOneofCase.RegisterAgentType:
                await RegisterAgentTypeAsync(connection, message.RegisterAgentType);
                break;
            default:
                throw new InvalidOperationException($"Unknown message type for message '{message}'.");
        };
    }

    async ValueTask RegisterAgentTypeAsync(WorkerProcessConnection connection, RegisterAgentType msg)
    {
        await ClusterClient.GetGrain<IWorkerAgentRegistryGrain>(0).RegisterAgentType(msg.Type, Reference);
    }

    async ValueTask DispatchEventAsync(WorkerProcessConnection connection, RpcEvent evt)
    {
        var topic = ClusterClient.GetStreamProvider("agents").GetStream<Event>(StreamId.Create(evt.Namespace, evt.Topic));
        await topic.OnNextAsync(evt.ToEvent());
    }

    async ValueTask DispatchRequestAsync(WorkerProcessConnection connection, RpcRequest request)
    {
        var requestId = request.RequestId;
        if (string.Equals("runtime", request.Target))
        {
            if (string.Equals("subscribe", request.Method))
            {
                await InvokeRequestDelegate(connection, request, async (_) =>
                {
                    await SubscribeToTopic(ClusterClient, connection.ResponseStream, request);
                    return new RpcResponse { Result = "Ok" };
                });

                return;
            }
        }
        else
        {
            await InvokeRequestDelegate(connection, request, async request =>
            {
                var agent = ClusterClient.GetGrain<IAgentGrain>(GrainId.Create(request.Target.Type, request.Target.Key));
                var result = await agent.Invoke(request.Method, request.Params.ToDictionary(keySelector => keySelector.Key, kvp => JsonSerializer.Deserialize<object>(kvp.Value)));
                return new RpcResponse { Result = JsonSerializer.Serialize(result) };
            });
        }
    }

    private async Task InvokeRequestDelegate(WorkerProcessConnection connection, RpcRequest request, Func<RpcRequest, Task<RpcResponse>> func)
    {
        try
        {
            var response = await func(request);
            response.RequestId = request.RequestId;
            await connection.ResponseStream.WriteAsync(new Message { Response = response });
        }
        catch (Exception ex)
        {
            await connection.ResponseStream.WriteAsync(new Message { Response = new RpcResponse { RequestId = request.RequestId, Error = ex.Message } });
        }
    }

    private static async ValueTask SubscribeToTopic(IClusterClient orleansClient, IServerStreamWriter<Message> responseStream, RpcRequest request)
    {
        // Subscribe to a topic
        var ns = request.Params["ns"];
        var topicName = request.Params["topic"];
        var topic = orleansClient.GetStreamProvider("agents").GetStream<Event>(StreamId.Create(ns, topicName));
        await topic.SubscribeAsync(OnNextAsync);
        return;

        async Task OnNextAsync(IList<SequentialItem<Event>> items)
        {
            foreach (var item in items)
            {
                var evt = item.Item.ToRpcEvent();
                evt.Namespace = ns;
                evt.Topic = topicName;
                evt.Data["sequenceId"] = item.Token.SequenceNumber.ToString();
                evt.Data["eventIdx"] = item.Token.EventIndex.ToString();
                await responseStream.WriteAsync(new Message { Event = evt });
            }
        }
    }

    void DispatchResponse(WorkerProcessConnection connection, RpcResponse response)
    {
        // Send response to caller.
    }

    internal async Task ConnectToWorkerProcess(IAsyncStreamReader<Message> requestStream, IServerStreamWriter<Message> responseStream, ServerCallContext context)
    {
        var workerProcess = new WorkerProcessConnection(this, requestStream, responseStream);
        _
    }
}

public interface IAgentGrain : IGrainWithStringKey
{
    Task<object> Invoke(string method, Dictionary<string, object?> parameters);
}

internal sealed class WorkerProcessConnection
{
    private readonly Task _completionTask;

    public WorkerProcessConnection(WorkerGateway agentWorker, IAsyncStreamReader<Message> requestStream, IServerStreamWriter<Message> responseStream)
    {
        _gateway = agentWorker;
        RequestStream = requestStream;
        ResponseStream = responseStream;
        _completionTask = Start();
    }

    private readonly WorkerGateway _gateway;

    public IAsyncStreamReader<Message> RequestStream { get; }
    public IServerStreamWriter<Message> ResponseStream { get; }

    public async Task SendMessage(Message message)
    {
        await ResponseStream.WriteAsync(message);
    }

    public Task Completion => _completionTask;


    public Task Start()
    {
        var didSuppress = false;
        if (!ExecutionContext.IsFlowSuppressed())
        {
            didSuppress = true;
            ExecutionContext.SuppressFlow();
        }

        try
        {
            return Task.Run(Run);
        }
        finally
        {
            if (didSuppress)
            {
                ExecutionContext.RestoreFlow();
            }
        }
    }

    public async Task Run()
    {
        await Task.Yield();
        await foreach (var message in RequestStream.ReadAllAsync())
        {
            await _gateway.OnReceivedMessageAsync(this, message);
        }
    }
}

public static class RpcEventExtensions
{
    public static RpcEvent ToRpcEvent(this Event input)
    {
        var result = new RpcEvent
        {
            Type = input.Type,
            Subject = input.Subject
        };

        if (input.Data is not null)
        {
            result.Data.Add(input.Data);
        }

        return result;
    }

    public static Event ToEvent(this RpcEvent input)
    {
        var result = new Event
        {
            Type = input.Type,
            Subject = input.Subject,
            Data = []
        };

        if (input.Data is not null)
        {
            foreach (var kvp in input.Data)
            {
                result.Data[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }
}
