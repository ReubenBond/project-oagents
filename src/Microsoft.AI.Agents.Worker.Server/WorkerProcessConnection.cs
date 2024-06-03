using Grpc.Core;
using Agents;
using Orleans.Runtime;

namespace Microsoft.AI.Agents.Worker;

internal sealed class WorkerProcessConnection
{
    private readonly object _lock = new();
    private readonly HashSet<string> _supportedTypes = [];
    private readonly WorkerGateway _gateway;

    public WorkerProcessConnection(WorkerGateway agentWorker, IAsyncStreamReader<Message> requestStream, IServerStreamWriter<Message> responseStream, ServerCallContext context)
    {
        _gateway = agentWorker;
        RequestStream = requestStream;
        ResponseStream = responseStream;
        ServerCallContext = context;
        Completion = Start();
    }

    public IAsyncStreamReader<Message> RequestStream { get; }
    public IServerStreamWriter<Message> ResponseStream { get; }
    public ServerCallContext ServerCallContext { get; }

    public void AddSupportedType(string type)
    {
        lock (_lock)
        {
            _supportedTypes.Add(type);
        }
    }

    public HashSet<string> GetSupportedTypes()
    {
        lock (_lock)
        {
            return new HashSet<string>(_supportedTypes);
        }
    }

    public async Task SendMessage(Message message)
    {
        await ResponseStream.WriteAsync(message);
    }

    public Task Completion { get; }

    private Task Start()
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
        try
        {
            await foreach (var message in RequestStream.ReadAllAsync())
            {
                await _gateway.OnReceivedMessageAsync(this, message);
            }
        }
        finally
        {
            _gateway.OnRemoveWorkerProcess(this);
        }
    }
}

internal interface IAgentStateGrain : IGrainWithStringKey
{
    ValueTask<(Dictionary<string, object> State, string ETag)> ReadStateAsync();
    ValueTask<string> WriteStateAsync(Dictionary<string, object> state, string eTag);
}

internal sealed class AgentStateGrain([PersistentState("state", "agent-state")] IPersistentState<Dictionary<string, object>> state) : Grain, IAgentStateGrain
{
    public ValueTask<(Dictionary<string, object> State, string ETag)> ReadStateAsync()
    {
        return new((state.State, state.Etag));
    }

    public async ValueTask<string> WriteStateAsync(Dictionary<string, object> value, string eTag)
    {
        if (string.Equals(state.Etag, eTag, StringComparison.Ordinal))
        {
            state.State = value;
            await state.WriteStateAsync();
        }

        return state.Etag;
    }
}
