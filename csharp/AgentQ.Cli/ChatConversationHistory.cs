using AgentQ.Core.Models;

namespace AgentQ.Cli;

/// <summary>
/// Mutable CLI conversation history.
/// </summary>
public class ChatConversationHistory
{
    private readonly List<ChatMessage> _messages = new();

    /// <summary>
    /// Current conversation messages.
    /// </summary>
    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    /// <summary>
    /// Adds a user text message.
    /// </summary>
    public void AddUserMessage(string text)
    {
        _messages.Add(ChatMessage.UserText(text));
    }

    /// <summary>
    /// Adds an assistant message.
    /// </summary>
    public void AddAssistantMessage(List<ChatContent> content)
    {
        _messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = content
        });
    }

    /// <summary>
    /// Adds tool result content as a user-role message.
    /// </summary>
    public void AddToolResults(List<ChatContent> results)
    {
        _messages.Add(new ChatMessage
        {
            Role = ChatRole.User,
            Content = results
        });
    }

    /// <summary>
    /// Clears the history.
    /// </summary>
    public void Clear()
    {
        _messages.Clear();
    }

    /// <summary>
    /// Appends multiple messages to the history.
    /// </summary>
    public void AddRange(IEnumerable<ChatMessage> messages)
    {
        _messages.AddRange(messages);
    }

    /// <summary>
    /// Replaces older messages with one summary while preserving the recent tail.
    /// </summary>
    public int CompactWithSummary(ChatMessage summaryMessage, int keepLastMessages)
    {
        if (_messages.Count <= keepLastMessages)
        {
            return 0;
        }

        keepLastMessages = Math.Max(0, keepLastMessages);
        var preservedCount = Math.Min(keepLastMessages, _messages.Count);
        var compactedCount = _messages.Count - preservedCount;
        compactedCount = MoveBoundaryBeforeToolProtocolPair(compactedCount);
        if (compactedCount <= 0)
        {
            return 0;
        }

        var tail = _messages.Skip(compactedCount).ToList();

        _messages.Clear();
        _messages.Add(summaryMessage);
        _messages.AddRange(tail);

        return compactedCount;
    }

    /// <summary>
    /// Number of messages in history.
    /// </summary>
    public int MessageCount => _messages.Count;

    private int MoveBoundaryBeforeToolProtocolPair(int compactedCount)
    {
        while (compactedCount > 0 &&
               compactedCount < _messages.Count &&
               IsUserToolResultMessage(_messages[compactedCount]) &&
               IsAssistantToolUseMessage(_messages[compactedCount - 1]))
        {
            compactedCount--;
        }

        return compactedCount;
    }

    private static bool IsAssistantToolUseMessage(ChatMessage message)
    {
        return message.Role == ChatRole.Assistant &&
               message.Content.Any(content => content.Type == ContentType.ToolUse);
    }

    private static bool IsUserToolResultMessage(ChatMessage message)
    {
        return message.Role == ChatRole.User &&
               message.Content.Any(content => content.Type == ContentType.ToolResult);
    }
}
