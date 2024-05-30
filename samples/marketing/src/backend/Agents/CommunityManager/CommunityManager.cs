using Marketing.Events;
using Marketing.Options;
using Microsoft.AI.Agents.Abstractions;
using Microsoft.AI.Agents.Orleans;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Orleans.Runtime;

namespace Marketing.Agents;

[ImplicitStreamSubscription(Consts.OrleansNamespace)]
public partial class CommunityManager(
    [PersistentState("state", "messages")] IPersistentState<AgentState<CommunityManagerState>> state,
    Kernel kernel, 
    ISemanticTextMemory memory,
    ILogger<GraphicDesigner> logger) : AiAgent<CommunityManagerState>(state, memory, kernel)
{
    private readonly ILogger<GraphicDesigner> _logger = logger;

    public async override Task HandleEvent(Event item)
    {
        ArgumentNullException.ThrowIfNull(item);
        switch (item.Type)
        {
            case nameof(EventTypes.UserConnected):
                // The user reconnected, let's send the last message if we have one
                var lastMessage = State.State.History.LastOrDefault()?.Message;
                if (lastMessage is null)
                {
                    return;
                }

                await SendDesignedCreatedEvent(lastMessage, item.Data["UserId"]);
                break;

            case nameof(EventTypes.ArticleCreated):
                {
                    var article = item.Data["article"];
                    LogEvent(nameof(EventTypes.ArticleCreated), $"Article: {article}");
                    var context = new KernelArguments { ["input"] = AppendChatHistory(article) };
                    string socialMediaPost = await CallFunction(CommunityManagerPrompts.WritePost, context);
                    State.State.Data.WrittenSocialMediaPost = socialMediaPost;
                    await SendDesignedCreatedEvent(socialMediaPost, item.Data["UserId"]);
                    break;
                }
            default:
                break;
        }
    }

    private async Task SendDesignedCreatedEvent(string socialMediaPost, string userId)
    {
        await PublishEvent(Consts.OrleansNamespace, this.GetPrimaryKeyString(), new Event
        {
            Type = nameof(EventTypes.SocialMediaPostCreated),
            Data = new Dictionary<string, string> {
                            { "UserId", userId },
                            { nameof(socialMediaPost), socialMediaPost}
                        }
        });
    }

    public Task<string> GetArticle()
    {
        return Task.FromResult(State.State.Data.WrittenSocialMediaPost);
    }

    private void LogEvent(string eventType, string eventInfo) => LogEventCore(_logger, IdentityString, eventType, eventInfo);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "[{Agent}] Event {EventType}: {EventInfo}")]
    private static partial void LogEventCore(ILogger logger, string agent, string eventType, string eventInfo);
}