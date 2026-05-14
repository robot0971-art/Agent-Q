using AgentQ.Core.Models;

namespace AgentQ.Cli;

internal sealed class StreamedAssistantResponse
{
    public required IReadOnlyList<ChatContent> AssistantContent { get; init; }

    public required IReadOnlyList<ChatContent> ToolUses { get; init; }
}
