using System.Text;
using Microsoft.AI.Agents.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Orleans.Runtime;

namespace Microsoft.AI.Agents.Orleans;

public abstract class AiAgent<TState>(
    [PersistentState("state", "messages")] IPersistentState<AgentState<TState>> state,
    ISemanticTextMemory memory,
    Kernel kernel) : Agent, IAiAgent where TState : class, new()
{
    private readonly ISemanticTextMemory _memory = memory;

    protected IPersistentState<AgentState<TState>> State => state;
    protected Kernel Kernel => kernel;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Initialize the Agent state
        State.State.History ??= [];
        State.State.Data ??= new TState();

        return base.OnActivateAsync(cancellationToken);
    }

    public void AddToHistory(string message, ChatUserType userType) => State.State.History.Add(new ChatHistoryItem
    {
        Message = message,
        Order = State.State.History.Count + 1,
        UserType = userType
    });

    public string AppendChatHistory(string ask)
    {
        AddToHistory(ask, ChatUserType.User);
        return string.Join("\n", State.State.History.Select(message => $"{message.UserType}: {message.Message}"));
    }

    public virtual async Task<string> CallFunction(string template, KernelArguments arguments, OpenAIPromptExecutionSettings? settings = null)
    {
        // TODO: extract this to be configurable
        var propmptSettings = settings ?? new OpenAIPromptExecutionSettings { MaxTokens = 4096, Temperature = 0.8, TopP = 1 };
        var function = Kernel.CreateFunctionFromPrompt(template, propmptSettings);
        var result = (await Kernel.InvokeAsync(function, arguments)).ToString();
        AddToHistory(result, ChatUserType.Agent);
        await State.WriteStateAsync();
        return result;
    }

    /// <summary>
    /// Adds knowledge to the provided <paramref name="arguments"/>.
    /// </summary>
    /// <param name="instruction">The instruction string that uses the value of !index! as a placeholder to inject the data. Example:"Consider the following architectural guidelines: {waf}" </param>
    /// <param name="index">Knowledge index</param>
    /// <param name="arguments">The sk arguments, "input" is the argument </param>
    /// <returns>The updated arguments.</returns>
    public async Task<KernelArguments> AddKnowledge(string instruction, string index, KernelArguments arguments)
    {
        var documents = _memory.SearchAsync(index, arguments["input"]?.ToString()!, 5);
        var kbStringBuilder = new StringBuilder();
        await foreach (var doc in documents)
        {
            kbStringBuilder.AppendLine($"{doc.Metadata.Text}");
        }
        arguments[index] = instruction.Replace($"!{index}!", $"{kbStringBuilder}");
        return arguments;
    }
}