using Agents;

namespace Microsoft.AI.Agents.Worker;

public interface IAgentWorkerRegistryGrain : IGrainWithIntegerKey
{
    ValueTask RegisterAgentType(string type, IWorkerGateway worker);
    ValueTask UnregisterAgentType(string type, IWorkerGateway worker);
    ValueTask AddWorker(IWorkerGateway worker);
    ValueTask RemoveWorker(IWorkerGateway worker);
    ValueTask<IWorkerGateway?> GetCompatibleWorker(string type);
    ValueTask<IWorkerGateway?> GetOrPlaceAgent(AgentId agentId);
}
