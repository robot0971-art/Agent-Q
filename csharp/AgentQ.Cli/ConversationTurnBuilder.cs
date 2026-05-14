using AgentQ.Core.Models;
using AgentQ.Tools;

namespace AgentQ.Cli;

internal sealed class ConversationTurnBuilder
{
    public ConversationTurnRequest Build(
        string model,
        ChatConversationHistory history,
        ToolRegistry registry,
        int stepLimit,
        uint maxTokens)
    {
        return new ConversationTurnRequest
        {
            Context = new ChatContext
            {
                Model = model,
                SystemPrompt = SystemPromptManager.BuildDefaultPrompt(),
                Messages = history.Messages.ToList(),
                MaxTokens = maxTokens,
                Stream = true,
                MaxSteps = stepLimit
            },
            Tools = registry.GetToolDefinitions().Select(t => new ToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.InputSchema
            }).ToList()
        };
    }
}
