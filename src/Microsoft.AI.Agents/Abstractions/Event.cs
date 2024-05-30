using System.Runtime.Serialization;

namespace Microsoft.AI.Agents.Abstractions
{
    [DataContract]
    public class Event
    {
        public required Dictionary<string, string> Data { get; init; }
        public required string Type { get; init; }
        public string? Subject { get; init; }
    }
}