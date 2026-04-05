using AgentQ.Api;

namespace AgentQ.MockService;

public static class ResponseBuilder
{
    private const string DefaultModel = "claude-sonnet-4-6";

    public static MessageResponse TextMessage(string id, string text)
    {
        return new MessageResponse
        {
            Id = id,
            Type = "message",
            Role = "assistant",
            Content = new List<OutputContentBlock>
            {
                new()
                {
                    Type = OutputContentBlockType.Text,
                    Text = text
                }
            },
            Model = DefaultModel,
            StopReason = "end_turn",
            Usage = new Usage
            {
                InputTokens = 10,
                OutputTokens = 6
            }
        };
    }

    public static MessageResponse TextMessageWithUsage(string id, string text, uint inputTokens, uint outputTokens)
    {
        return new MessageResponse
        {
            Id = id,
            Type = "message",
            Role = "assistant",
            Content = new List<OutputContentBlock>
            {
                new()
                {
                    Type = OutputContentBlockType.Text,
                    Text = text
                }
            },
            Model = DefaultModel,
            StopReason = "end_turn",
            Usage = new Usage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            }
        };
    }

    public static MessageResponse ToolMessage(string id, string toolId, string toolName, object input)
    {
        return ToolMessages(id, new[] { new ToolUseMessage(toolId, toolName, input) });
    }

    public static MessageResponse ToolMessages(string id, ToolUseMessage[] toolUses)
    {
        var content = new List<OutputContentBlock>();
        foreach (var toolUse in toolUses)
        {
            content.Add(new OutputContentBlock
            {
                Type = OutputContentBlockType.ToolUse,
                Id = toolUse.ToolId,
                Name = toolUse.ToolName,
                Input = toolUse.Input
            });
        }

        return new MessageResponse
        {
            Id = id,
            Type = "message",
            Role = "assistant",
            Content = content,
            Model = DefaultModel,
            StopReason = "tool_use",
            Usage = new Usage
            {
                InputTokens = 10,
                OutputTokens = 3
            }
        };
    }
}

public record ToolUseMessage(string ToolId, string ToolName, object Input);

