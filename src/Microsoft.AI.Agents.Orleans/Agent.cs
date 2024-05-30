using Microsoft.AI.Agents.Abstractions;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace Microsoft.AI.Agents.Orleans;

public abstract class Agent : Grain, IGrainWithStringKey, IAgent, IStreamSubscriptionObserver
{
    public abstract Task HandleEvent(Event item);

    private async Task HandleEvent(Event item, StreamSequenceToken? token)
    {
        await HandleEvent(item);
    }

    public async Task PublishEvent(string ns, string id, Event item)
    {
        var streamProvider = this.GetStreamProvider("StreamProvider");
        var streamId = StreamId.Create(ns, id);
        var stream = streamProvider.GetStream<Event>(streamId);
        await stream.OnNextAsync(item);
    }

    async Task IStreamSubscriptionObserver.OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        var streamSubscriptionHandle = handleFactory.Create<Event>();
        await streamSubscriptionHandle.ResumeAsync(HandleEvent);
    }
}
