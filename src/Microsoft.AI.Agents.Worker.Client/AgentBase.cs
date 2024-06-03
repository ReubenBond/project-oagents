using Agents;
using Event = Microsoft.AI.Agents.Abstractions.Event;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.AI.Agents.Worker.Client;

public abstract class AgentBase
{
    protected internal AgentId AgentId => _context.AgentId;
    private readonly object _lock = new();
    private readonly Dictionary<string, TaskCompletionSource<RpcResponse>> _pendingRequests = [];
    private readonly Channel<object> _mailbox = Channel.CreateUnbounded<object>();
    private readonly IAgentContext _context;

    protected AgentBase(IAgentContext context)
    {
        _context = context;
        context.AgentInstance = this;
        Completion = Start();
    }

    internal Task Completion { get; }

    internal Task Start()
    {
        var didSuppress = false;
        if (!ExecutionContext.IsFlowSuppressed())
        {
            didSuppress = true;
            ExecutionContext.SuppressFlow();
        }

        try
        {
            return Task.Run(RunMessagePump);
        }
        finally
        {
            if (didSuppress)
            {
                ExecutionContext.RestoreFlow();
            }
        }
    }

    internal void ReceiveMessage(Message message) => _mailbox.Writer.TryWrite(message);

    private async Task RunMessagePump()
    {
        await Task.Yield();
        await foreach (var message in _mailbox.Reader.ReadAllAsync())
        {
            try
            {
                switch (message)
                {
                    case Message msg:
                        await HandleRpcMessage(msg);
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected message '{message}'.");
                }
            }
            catch (Exception ex)
            {
                _context.Logger.LogError(ex, "Error processing message.");
            }
        }
    }

    private async Task HandleRpcMessage(Message msg)
    {
        switch (msg.MessageCase)
        {
            case Message.MessageOneofCase.Event:
                await HandleEvent(msg.Event.Namespace, msg.Event.Topic, msg.Event.ToEvent());
                break;
            case Message.MessageOneofCase.Request:
                await OnRequestCore(msg.Request);
                break;
            case Message.MessageOneofCase.Response:
                OnResponseCore(msg.Response);
                break;
        }
    }

    private void OnResponseCore(RpcResponse response)
    {
        var requestId = response.RequestId;
        TaskCompletionSource<RpcResponse>? completion;
        lock (_lock)
        {
            if (!_pendingRequests.Remove(requestId, out completion))
            {
                throw new InvalidOperationException($"Unknown request id '{requestId}'.");
            }
        }

        completion.SetResult(response);
    }

    private async ValueTask OnRequestCore(RpcRequest request)
    {
        RpcResponse response;

        try
        {
            response = await OnRequest(request);
        }
        catch (Exception ex)
        {
            response = new RpcResponse { Error = ex.Message };
        }

        await _context.SendResponseAsync(request, response);
    }

    public async ValueTask<RpcResponse> RequestAsync(AgentId target, string method, Dictionary<string, string> parameters)
    {
        var requestId = Guid.NewGuid().ToString();
        var request = new RpcRequest
        {
            Target = target,
            RequestId = requestId,
            Method = method,
            Params = { parameters }
        };

        var completion = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pendingRequests[requestId] = completion;
        }

        await _context.SendRequestAsync(this, request);
        return await completion.Task;
    }

    protected internal async ValueTask PublishEvent(string ns, string topic, Event @event)
    {
        var rpcEvent = @event.ToRpcEvent();
        rpcEvent.Namespace = ns;
        rpcEvent.Topic = topic;
        await _context.PublishEventAsync(rpcEvent);
    }

    protected internal virtual ValueTask<RpcResponse> OnRequest(RpcRequest request) => new(new RpcResponse { Error = "Not implemented" });

    protected internal virtual ValueTask HandleEvent(string ns, string topic, Event @event) => default;
}
