using AgentQ.Core.Models;

namespace AgentQ.Cli;

/// <summary>
/// 대화 기록 관리
/// </summary>
public class ChatConversationHistory
{
    private readonly List<ChatMessage> _messages = new();

    /// <summary>
    /// 메시지 목록
    /// </summary>
    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    /// <summary>
    /// 사용자 메시지 추가
    /// </summary>
    /// <param name="text">메시지 텍스트</param>
    public void AddUserMessage(string text)
    {
        _messages.Add(ChatMessage.UserText(text));
    }

    /// <summary>
    /// 어시스턴트 메시지 추가
    /// </summary>
    /// <param name="content">메시지 내용</param>
    public void AddAssistantMessage(List<ChatContent> content)
    {
        _messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = content
        });
    }

    /// <summary>
    /// 도구 결과 추가
    /// </summary>
    /// <param name="results">결과 목록</param>
    public void AddToolResults(List<ChatContent> results)
    {
        _messages.Add(new ChatMessage
        {
            Role = ChatRole.User,
            Content = results
        });
    }

    /// <summary>
    /// 대화 기록 초기화
    /// </summary>
    public void Clear()
    {
        _messages.Clear();
    }

    /// <summary>
    /// 여러 메시지를 대화 기록에 추가
    /// </summary>
    /// <param name="messages">추가할 메시지 목록</param>
    public void AddRange(IEnumerable<ChatMessage> messages)
    {
        _messages.AddRange(messages);
    }

    /// <summary>
    /// 오래된 메시지를 요약 메시지 하나로 압축하고 최근 메시지는 유지합니다.
    /// </summary>
    /// <param name="summaryMessage">압축 요약 메시지</param>
    /// <param name="keepLastMessages">뒤에서 유지할 메시지 수</param>
    /// <returns>압축된 원본 메시지 수</returns>
    public int CompactWithSummary(ChatMessage summaryMessage, int keepLastMessages)
    {
        if (_messages.Count <= keepLastMessages)
        {
            return 0;
        }

        keepLastMessages = Math.Max(0, keepLastMessages);
        var preservedCount = Math.Min(keepLastMessages, _messages.Count);
        var compactedCount = _messages.Count - preservedCount;
        var tail = _messages.Skip(compactedCount).ToList();

        _messages.Clear();
        _messages.Add(summaryMessage);
        _messages.AddRange(tail);

        return compactedCount;
    }

    /// <summary>
    /// 메시지 개수
    /// </summary>
    public int MessageCount => _messages.Count;
}

