namespace Microsoft.AI.Agents.Abstractions;

public interface IAiAgentRuntime
{
    void AddToHistory(string message, string userType);
    string AppendChatHistory(string ask);
    Task<string> CallFunction(string template, KernelArguments arguments, OpenAIPromptExecutionSettings? settings = null);
    Task<KernelArguments> AddKnowledge(string instruction, string index, KernelArguments arguments);
}
