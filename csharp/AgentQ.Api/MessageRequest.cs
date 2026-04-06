namespace AgentQ.Api;

/// <summary>
/// 메시지 요청
/// </summary>
public class MessageRequest
{
    /// <summary>
    /// 사용할 모델
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 최대 토큰 수
    /// </summary>
    public uint MaxTokens { get; set; }

    /// <summary>
    /// 메시지 목록
    /// </summary>
    public List<InputMessage> Messages { get; set; } = new();

    /// <summary>
    /// 시스템 프롬프트
    /// </summary>
    public string? System { get; set; }

    /// <summary>
    /// 사용 가능한 도구 목록
    /// </summary>
    public List<ToolDefinition>? Tools { get; set; }

    /// <summary>
    /// 도구 선택 방식
    /// </summary>
    public ToolChoice? ToolChoice { get; set; }

    /// <summary>
    /// 스트리밍 사용 여부
    /// </summary>
    public bool Stream { get; set; }

    /// <summary>
    /// 스트리밍 모드로 설정
    /// </summary>
    /// <returns>현재 요청 (메서드 체이닝용)</returns>
    public MessageRequest WithStreaming()
    {
        Stream = true;
        return this;
    }
}

/// <summary>
/// 입력 메시지
/// </summary>
public class InputMessage
{
    /// <summary>
    /// 메시지 역할 (user, assistant, system 등)
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 메시지 내용 블록 목록
    /// </summary>
    public List<InputContentBlock> Content { get; set; } = new();

    /// <summary>
    /// 사용자 텍스트 메시지 생성
    /// </summary>
    /// <param name="text">텍스트 내용</param>
    /// <returns>사용자 메시지</returns>
    public static InputMessage UserText(string text)
    {
        return new InputMessage
        {
            Role = "user",
            Content = new List<InputContentBlock> { InputContentBlock.CreateText(text) }
        };
    }

    /// <summary>
    /// 사용자 도구 결과 메시지 생성
    /// </summary>
    /// <param name="toolUseId">도구 사용 ID</param>
    /// <param name="content">결과 내용</param>
    /// <param name="isError">오류 여부</param>
    /// <returns>도구 결과 메시지</returns>
    public static InputMessage UserToolResult(string toolUseId, string content, bool isError)
    {
        return new InputMessage
        {
            Role = "user",
            Content = new List<InputContentBlock>
            {
                InputContentBlock.CreateToolResult(toolUseId, content, isError)
            }
        };
    }
}

