using Azure.AI.OpenAI;
using Microsoft.AI.Agents.Abstractions;

namespace Microsoft.AI.Agents.Orleans;

[GenerateSerializer, Immutable]
public struct EventSurrogate
{
    [Id(0)]
    public Dictionary<string, string> Data { get; set; }
    [Id(1)]
    public string Type { get; set; }
    [Id(2)]
    public string? Subject { get; set; }
}

[RegisterConverter]
public sealed class EventSurrogateConverter :
    IConverter<Event, EventSurrogate>
{
    public Event ConvertFromSurrogate(
        in EventSurrogate surrogate) =>
        new Event { Data = surrogate.Data, Subject = surrogate.Subject, Type = surrogate.Type };

    public EventSurrogate ConvertToSurrogate(
        in Event value) =>
        new EventSurrogate
        {
            Data = value.Data,
            Type = value.Type,
            Subject = value.Subject
        };
}

public sealed class FunctionResult
{
    public Dictionary<string, object>? Metadata { get; set; }
    public object? Value { get; set; }
}

public sealed class FunctionCall
{
    public required string Name { get; init; }
    public Dictionary<string, object>? Arguments { get; init; } 
    public Dictionary<string, object>? Metadata { get; init; } 
}

public interface IExternalAgentGrain : IGrainWithStringKey
{
    ValueTask<FunctionResult> Invoke(FunctionCall function);
}

public sealed class ExternalAgentGrain : Grain, IExternalAgentGrain
{
    public ValueTask<FunctionResult> Invoke(FunctionCall function)
    {
        throw new NotImplementedException();
    }
}


