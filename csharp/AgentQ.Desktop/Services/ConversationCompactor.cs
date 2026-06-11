using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using AgentQ.Core.Models;

namespace AgentQ.Desktop.Services;

public sealed class ConversationCompactor
{
    private static readonly Regex ImportantToolLinePattern = new(
        @"(error|failed|failure|exception|timeout|denied|blocked|not found|exit code|status\s+\d{3}|http\s+\d{3}|[A-Za-z]:\\|/[^ \t\r\n]+|\\[^ \t\r\n]+|\b(?:npm|dotnet|python|git|node|pnpm|yarn)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImportantTextLinePattern = new(
        @"(latest user request|current task contract|current completion target|required actions|done when|invalid completions|required completion evidence|planId|planHash|workspace|verification|next action|remaining|blocked|failed|error)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public List<ChatMessage> Compact(
        List<ChatMessage> messages,
        int maxEstimatedTokens = 80_000,
        int keepRecentTurns = 4)
    {
        if (messages == null || messages.Count == 0)
        {
            return new List<ChatMessage>();
        }

        var totalTokens = EstimateTokens(messages);
        if (totalTokens <= maxEstimatedTokens)
        {
            return messages.ToList();
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
                    var importantContext = ExtractImportantTextContext(text);
                    if (importantContext.Count > 0)
                    {
                        summary += "\n\nPreserved priority context:\n" +
                                   string.Join("\n", importantContext.Select(line => $"- {line}"));
                    }

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
        sb.AppendLine("[Compacted Tool Use]");

        foreach (var content in msg.Content)
        {
            if (content.Type == ContentType.ToolUse)
            {
                sb.AppendLine($"- Tool: {content.ToolName}");
                sb.AppendLine($"- Tool use id: {content.ToolId}");
                sb.AppendLine($"- Input: {FormatToolInput(content.ToolInput)}");
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
        sb.AppendLine("[Compacted Tool Result]");

        foreach (var content in msg.Content)
        {
            if (content.Type == ContentType.ToolResult)
            {
                var status = content.IsToolError == true ? "error" : "success";
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
                sb.AppendLine($"- Tool use id: {content.ToolUseId}");
                sb.AppendLine($"- Status: {status}");
                sb.AppendLine($"- Output length: {resultLength} chars");
                sb.AppendLine("- Preview:");
                sb.AppendLine(snippet);
                var importantContext = ExtractImportantToolContext(content.ToolResult);
                if (importantContext.Count > 0)
                {
                    sb.AppendLine("Important evidence:");
                    foreach (var line in importantContext)
                    {
                        sb.AppendLine($"- {line}");
                    }
                }
            }
        }

        newMsg.Content.Add(ChatContent.CreateText(sb.ToString()));
        newMsg.CompactionSummary = sb.ToString();
        return newMsg;
    }

    private static string FormatToolInput(object? toolInput)
    {
        if (toolInput == null)
        {
            return "{}";
        }

        try
        {
            var json = JsonSerializer.Serialize(toolInput);
            if (!string.IsNullOrWhiteSpace(json))
            {
                return json.Length <= 500 ? json : json[..500] + "...";
            }
        }
        catch
        {
            // Best-effort formatting only; fall back to ToString below.
        }

        var text = toolInput.ToString() ?? string.Empty;
        return text.Length <= 500 ? text : text[..500] + "...";
    }

    private static IReadOnlyList<string> ExtractImportantToolContext(string? toolResult)
    {
        if (string.IsNullOrWhiteSpace(toolResult))
        {
            return [];
        }

        return toolResult
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && ImportantToolLinePattern.IsMatch(line))
            .Select(line => line.Length <= 240 ? line : line[..240] + "...")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static IReadOnlyList<string> ExtractImportantTextContext(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && ImportantTextLinePattern.IsMatch(line))
            .Select(line => line.Length <= 240 ? line : line[..240] + "...")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
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
