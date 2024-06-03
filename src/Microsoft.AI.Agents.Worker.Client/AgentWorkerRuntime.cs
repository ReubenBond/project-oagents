using Agents;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using RpcEvent = Agents.Event;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AI.Agents.Worker.Client;

public sealed class AgentWorkerRuntime(AgentRpc.AgentRpcClient client, IHostApplicationLifetime hostApplicationLifetime, IServiceProvider serviceProvider) : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, Type> _agentTypes = new();
    private readonly ConcurrentDictionary<(string Type, string Key), AgentBase> _agents = new();
    private readonly ConcurrentDictionary<string, (AgentBase Agent, string OriginalRequestId)> _pendingRequests = new();
    private readonly AsyncDuplexStreamingCall<Message, Message> _channel = client.OpenChannel();
    private Task? _runTask;

    public void Dispose()
    {
        _channel.Dispose();
    }

    private async Task RunMessagePump()
    {
        await foreach (var message in _channel.ResponseStream.ReadAllAsync(hostApplicationLifetime.ApplicationStopping))
        {
            switch (message.MessageCase)
            {
                case Message.MessageOneofCase.Request:
                    GetOrActivateAgent(message.Request.Target).ReceiveMessage(message);
                    break;
                case Message.MessageOneofCase.Response:
                    if (!_pendingRequests.TryRemove(message.Response.RequestId, out var request))
                    {
                        throw new InvalidOperationException($"Unexpected response '{message.Response}'");
                    }

                    message.Response.RequestId = request.OriginalRequestId;
                    request.Agent.ReceiveMessage(message);
                    break;
                case Message.MessageOneofCase.Event:
                    foreach (var agent in _agents.Values)
                    {
                        agent.ReceiveMessage(message);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected message '{message}'.");
            }
        }
    }

    private AgentBase GetOrActivateAgent(AgentId agentId)
    {
        if (!_agents.TryGetValue((agentId.Type, agentId.Key), out var agent))
        {
            if (_agentTypes.TryGetValue(agentId.Type, out var agentType))
            {
                var context = new AgentContext(agentId, this, serviceProvider.GetRequiredService<ILogger<AgentBase>>());
                agent = (AgentBase)ActivatorUtilities.CreateInstance(serviceProvider, agentType, context);
                _agents.TryAdd((agentId.Type, agentId.Key), agent);
            }
            else
            {
                throw new InvalidOperationException($"Agent type '{agentId.Type}' is unknown.");
            }
        }

        return agent;
    }

    public async ValueTask RegisterAgentType<TAgent>(string type) where TAgent : AgentBase
    {
        if (_agentTypes.TryAdd(type, typeof(TAgent)))
        {
            await _channel.RequestStream.WriteAsync(new Message
            {
                RegisterAgentType = new RegisterAgentType
                {
                    Type = type,
                }
            });
        }
    }

    public async ValueTask SendResponse(RpcResponse response)
    {
        await _channel.RequestStream.WriteAsync(new Message { Response = response });
    }

    public async ValueTask SendRequest(AgentBase agent, RpcRequest request)
    {
        var requestId = Guid.NewGuid().ToString();
        _pendingRequests[requestId] = (agent, request.RequestId);
        request.RequestId = requestId;
        await _channel.RequestStream.WriteAsync(new Message { Request = request });
    }

    public async ValueTask PublishEvent(RpcEvent @event)
    {
        await _channel.RequestStream.WriteAsync(new Message { Event = @event });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runTask = Start();
        return Task.CompletedTask;
    }

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

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Dispose();
        if (_runTask is { } task)
        {
            await task;
        }
    }
}
