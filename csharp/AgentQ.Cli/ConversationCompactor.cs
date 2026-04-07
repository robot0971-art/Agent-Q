using System.Text;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;

namespace AgentQ.Cli;

/// <summary>
/// 대화 기록을 요약 메시지로 압축합니다.
/// </summary>
public sealed class ConversationCompactor
{
    private const int DefaultKeepLastMessages = 6;

    /// <summary>
    /// 오래된 대화 기록을 요약해 압축합니다.
    /// </summary>
    public async Task<CompactResult> CompactAsync(
        ILlmProvider provider,
        string model,
        ChatConversationHistory history,
        CancellationToken ct = default)
    {
        if (history.MessageCount <= DefaultKeepLastMessages)
        {
            return CompactResult.Skipped("Not enough messages to compact.");
        }

        var messagesToSummarize = history.Messages
            .Take(history.MessageCount - DefaultKeepLastMessages)
            .ToList();

        if (messagesToSummarize.Count == 0)
        {
            return CompactResult.Skipped("Not enough messages to compact.");
        }

        var context = new ChatContext
        {
            Model = model,
            Stream = false,
            MaxTokens = 400,
            Messages =
            [
                ChatMessage.SystemText(
                    "Summarize the prior conversation for continued coding work. " +
                    "Keep it compact, factual, and action-oriented. Include goals, key decisions, important file paths, open issues, and unresolved risks. " +
                    "Do not include filler or markdown."),
                ChatMessage.UserText(BuildSummaryPrompt(messagesToSummarize))
            ]
        };

        var response = await provider.GenerateResponseAsync(context, [], ct);
        var summary = string.Join(
            "\n",
            response.Content
                .Where(content => content.Type == ContentType.Text)
                .Select(content => content.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(summary))
        {
            return CompactResult.Skipped("Provider returned an empty summary.");
        }

        var compactedCount = history.CompactWithSummary(
            ChatMessage.SystemText($"Conversation summary:\n{summary.Trim()}"),
            DefaultKeepLastMessages);

        return new CompactResult(true, compactedCount, history.MessageCount);
    }

    private static string BuildSummaryPrompt(IEnumerable<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Summarize these earlier messages for future turns:");
        builder.AppendLine();

        foreach (var message in messages)
        {
            builder.AppendLine($"{message.Role}: {FormatMessage(message)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatMessage(ChatMessage message)
    {
        return string.Join(
            " | ",
            message.Content.Select(content => content.Type switch
            {
                ContentType.Text => content.Text ?? string.Empty,
                ContentType.ToolUse => $"tool_use:{content.ToolName} {content.ToolInput}",
                ContentType.ToolResult => $"tool_result:{content.ToolUseId} error={content.IsToolError == true} {content.ToolResult}",
                _ => string.Empty
            }).Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}

/// <summary>
/// compact 실행 결과
/// </summary>
public sealed record CompactResult(bool Applied, int CompactedMessages, int TotalMessagesAfter)
{
    public string? Reason { get; init; }

    public static CompactResult Skipped(string reason) => new(false, 0, 0) { Reason = reason };
}
