using AgentQ.Core.Models;

namespace AgentQ.Cli;

internal sealed class ConversationTurnRequest
{
    public required ChatContext Context { get; init; }

    public required IEnumerable<ToolDefinition> Tools { get; init; }
}
