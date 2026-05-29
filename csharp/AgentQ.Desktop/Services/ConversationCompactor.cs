using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgentQ.Core.Models;

namespace AgentQ.Desktop.Services;

public sealed class ConversationCompactor
{
    public List<ChatMessage> Compact(
        List<ChatMessage> messages,
        int maxEstimatedTokens = 80_000,
        int keepRecentTurns = 4)
    {
        if (messages == null || messages.Count == 0)
        {
            return messages ?? new List<ChatMessage>();
        }

        var totalTokens = EstimateTokens(messages);
        if (totalTokens <= maxEstimatedTokens)
        {
            return messages;
        }

        // We will keep System messages, and then compact the rest, keeping keepRecentTurns messages intact.
        var compactedMessages = new List<ChatMessage>();
        var systemMessages = messages.Where(m => m.Role == ChatRole.System).ToList();
        compactedMessages.AddRange(systemMessages);

        var nonSystemMessages = messages.Where(m => m.Role != ChatRole.System).ToList();
        
        // Let's determine how many turns we have. Each turn might consist of multiple messages (e.g. User and Assistant, or Tool uses).
        // For simplicity, we can just keep the last N messages of non-system messages.
        var keepCount = Math.Min(nonSystemMessages.Count, keepRecentTurns);
        var messagesToCompact = nonSystemMessages.Take(nonSystemMessages.Count - keepCount).ToList();
        var messagesToKeep = nonSystemMessages.Skip(nonSystemMessages.Count - keepCount).ToList();

        // Compact the messages to compact
        var summarizedTools = new List<ChatMessage>();
        var otherMessages = new List<ChatMessage>();

        foreach (var msg in messagesToCompact)
        {
            if (msg.Role == ChatRole.User && msg.Content.Any(c => c.Type == ContentType.ToolResult))
            {
                // This is a tool result message. Let's group it.
                var summarizedMsg = SummarizeToolResult(msg);
                compactedMessages.Add(summarizedMsg);
            }
            else if (msg.Role == ChatRole.Assistant && msg.Content.Any(c => c.Type == ContentType.ToolUse))
            {
                // This is a tool use message. Let's summarize it.
                var summarizedMsg = SummarizeToolUse(msg);
                compactedMessages.Add(summarizedMsg);
            }
            else
            {
                // For other user messages or assistant text response, we can keep the text or shorten it.
                var compactedMsg = CompactTextMessage(msg);
                compactedMessages.Add(compactedMsg);
            }
        }

        compactedMessages.AddRange(messagesToKeep);
        return compactedMessages;
    }

    private ChatMessage CompactTextMessage(ChatMessage msg)
    {
        var newMsg = new ChatMessage
        {
            Role = msg.Role,
            IsCompacted = true
        };

        foreach (var content in msg.Content)
        {
            if (content.Type == ContentType.Text && !string.IsNullOrEmpty(content.Text))
            {
                var text = content.Text;
                if (text.Length > 1000)
                {
                    // Shorten to last 2 paragraphs or first/last 500 chars
                    var summary = $"[Shortened text, original length: {text.Length} chars]\n" +
                                  text.Substring(0, 300) + "\n...\n" + text.Substring(text.Length - 300);
                    newMsg.Content.Add(ChatContent.CreateText(summary));
                    newMsg.CompactionSummary = $"Shortened text ({text.Length} -> {summary.Length} chars)";
                }
                else
                {
                    newMsg.Content.Add(ChatContent.CreateText(text));
                }
            }
            else
            {
                newMsg.Content.Add(content);
            }
        }

        return newMsg;
    }

    private ChatMessage SummarizeToolUse(ChatMessage msg)
    {
        var newMsg = new ChatMessage
        {
            Role = ChatRole.Assistant,
            IsCompacted = true
        };

        var sb = new StringBuilder();
        sb.AppendLine("[Tool Use Summary]");

        foreach (var content in msg.Content)
        {
            if (content.Type == ContentType.ToolUse)
            {
                sb.AppendLine($"🛠️ Call: {content.ToolName} (ID: {content.ToolId})");
            }
            else if (content.Type == ContentType.Text && !string.IsNullOrEmpty(content.Text))
            {
                var text = content.Text.Length > 200 ? content.Text.Substring(0, 200) + "..." : content.Text;
                sb.AppendLine(text);
            }
        }

        newMsg.Content.Add(ChatContent.CreateText(sb.ToString()));
        newMsg.CompactionSummary = sb.ToString();
        return newMsg;
    }

    private ChatMessage SummarizeToolResult(ChatMessage msg)
    {
        var newMsg = new ChatMessage
        {
            Role = ChatRole.User,
            IsCompacted = true
        };

        var sb = new StringBuilder();
        sb.AppendLine("[Tool Result Summary]");

        foreach (var content in msg.Content)
        {
            if (content.Type == ContentType.ToolResult)
            {
                var status = content.IsToolError == true ? "❌ Error" : "✅ Success";
                var resultLength = content.ToolResult?.Length ?? 0;
                var snippet = "";
                if (resultLength > 0 && content.ToolResult != null)
                {
                    var lines = content.ToolResult.Split('\n');
                    snippet = lines.Length > 3 
                        ? string.Join("\n", lines.Take(3)) + "\n..." 
                        : content.ToolResult;
                    if (snippet.Length > 300)
                    {
                        snippet = snippet.Substring(0, 300) + "...";
                    }
                }
                sb.AppendLine($"{status} toolUseId: {content.ToolUseId}. Output length: {resultLength} chars. Preview:\n{snippet}");
            }
        }

        newMsg.Content.Add(ChatContent.CreateText(sb.ToString()));
        newMsg.CompactionSummary = sb.ToString();
        return newMsg;
    }

    public static int EstimateTokens(IReadOnlyList<ChatMessage> messages)
    {
        if (messages == null) return 0;
        int totalChars = 0;
        foreach (var msg in messages)
        {
            foreach (var content in msg.Content)
            {
                if (content.Text != null) totalChars += content.Text.Length;
                if (content.ToolResult != null) totalChars += content.ToolResult.Length;
                if (content.ToolInput != null)
                {
                    totalChars += content.ToolInput.ToString()?.Length ?? 0;
                }
            }
        }
        return (int)Math.Ceiling(totalChars / 3.5);
    }
}
