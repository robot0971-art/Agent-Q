using AgentQ.Core.Models;

namespace AgentQ.Cli;

public class ChatConversationHistory
{
    private readonly List<ChatMessage> _messages = new();

    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    public void AddUserMessage(string text)
    {
        _messages.Add(ChatMessage.UserText(text));
    }

    public void AddAssistantMessage(List<ChatContent> content)
    {
        _messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = content
        });
    }

    public void AddToolResults(List<ChatContent> results)
    {
        _messages.Add(new ChatMessage
        {
            Role = ChatRole.User,
            Content = results
        });
    }

    public void Clear()
    {
        _messages.Clear();
    }

    public int MessageCount => _messages.Count;
}

