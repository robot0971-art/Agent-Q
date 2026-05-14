using System.Text;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;

namespace AgentQ.Cli;

internal sealed class StreamingProcessor
{
    public async Task<StreamedAssistantResponse> ProcessAsync(
        IAsyncEnumerable<StreamChunk> chunks,
        Action<string>? onTextDelta = null,
        CancellationToken ct = default)
    {
        var toolUses = new List<ChatContent>();
        var textBuilder = new StringBuilder();

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            if (chunk.TextDelta != null)
            {
                textBuilder.Append(chunk.TextDelta);
                onTextDelta?.Invoke(chunk.TextDelta);
            }

            if (chunk.ToolUseDelta?.IsComplete == true)
            {
                toolUses.Add(ChatContent.CreateToolUse(
                    chunk.ToolUseDelta.ToolId,
                    chunk.ToolUseDelta.ToolName,
                    chunk.ToolUseDelta.PartialInput ?? "{}"));
            }
        }

        var assistantContent = new List<ChatContent>();
        if (textBuilder.Length > 0)
        {
            assistantContent.Add(ChatContent.CreateText(textBuilder.ToString()));
        }

        assistantContent.AddRange(toolUses);

        return new StreamedAssistantResponse
        {
            AssistantContent = assistantContent,
            ToolUses = toolUses
        };
    }
}
