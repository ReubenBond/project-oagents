using Event = Microsoft.AI.Agents.Abstractions.Event;
using RpcEvent = Agents.Event;

namespace Microsoft.AI.Agents.Worker;

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
