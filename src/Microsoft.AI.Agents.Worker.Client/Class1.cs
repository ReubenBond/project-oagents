using Agents;
using Grpc.Core;
using Microsoft.AI.Agents.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace Microsoft.AI.Agents.Worker.Client;

public sealed class AgentWorkerRuntime(AgentRpc.AgentRpcClient client, IHostApplicationLifetime hostApplicationLifetime)
{
    private readonly ConcurrentDictionary<string, Type> _agentTypes = new();
    private readonly ConcurrentDictionary<(string Type, string Key), AgentBase> _agents = new();
    private AsyncDuplexStreamingCall<Message, Message> _channel;

    public async ValueTask Connect()
    {
        _channel = client.OpenChannel();
    }

    private async Task RunMessagePump()
    {
        await foreach (var message in _channel.ResponseStream.ReadAllAsync(hostApplicationLifetime.ApplicationStopping))
        {

        }
    }

    public async ValueTask RegisterAgentType<TAgent>(string type) where TAgent : AgentBase  
    {
        if (_agentTypes.TryAdd(type, typeof(TAgent)))
        {
            await client.
        }
    }

}

public abstract class AgentBase
{
    internal void ReceiveMessage(Message message)
    {

    }

    protected abstract ValueTask OnEvent(Event @event);
}
