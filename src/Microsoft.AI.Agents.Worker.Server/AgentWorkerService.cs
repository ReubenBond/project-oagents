﻿using Grpc.Core;
using Agents;

namespace Microsoft.AI.Agents.Worker;

// gRPC service which handles communication between the agent worker and the cluster.
internal class AgentWorkerService(WorkerGateway agentWorker) : AgentRpc.AgentRpcBase 
{
    public override async Task OpenChannel(IAsyncStreamReader<Message> requestStream, IServerStreamWriter<Message> responseStream, ServerCallContext context)
    {
        await agentWorker.ConnectToWorkerProcess(requestStream, responseStream, context);
    }
}
