using Microsoft.AI.Agents.Abstractions;

namespace Microsoft.AI.Agents.Worker.Client;

public abstract class Agent(IAgentContext context) : AgentBase(context), IAgent
{
    public abstract Task HandleEvent(Event item);

    protected internal override async ValueTask HandleEvent(string ns, string topic, Event item)
    {
        await HandleEvent(item);
    }

    async Task IAgent.PublishEvent(string ns, string id, Event item)
    {
        await PublishEvent(ns, id, item);
    }
}
