using Grpc.Core;
using Agents;

namespace Microsoft.AI.Agents.Worker;

internal sealed class WorkerProcessConnection
{
    private static long NextConnectionId;
    private readonly string _connectionId = Interlocked.Increment(ref NextConnectionId).ToString();
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

                // Fire and forget
                _gateway.OnReceivedMessageAsync(this, message).Ignore();
            }
        }
        finally
        {
            _gateway.OnRemoveWorkerProcess(this);
        }
    }

    public override string ToString() => $"Connection-{_connectionId}";
}
