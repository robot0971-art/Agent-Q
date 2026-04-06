using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentQ.Api;

/// <summary>
/// 메시지 응답
/// </summary>
public class MessageResponse
{
    /// <summary>
    /// 응답 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 응답 타입
    /// </summary>
    public string Type { get; set; } = "message";

    /// <summary>
    /// 응답 역할
    /// </summary>
    public string Role { get; set; } = "assistant";

    /// <summary>
    /// 응답 내용 블록 목록
    /// </summary>
    public List<OutputContentBlock> Content { get; set; } = new();

    /// <summary>
    /// 사용된 모델
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 중지 사유
    /// </summary>
    public string? StopReason { get; set; }

    /// <summary>
    /// 중지 시퀀스
    /// </summary>
    public string? StopSequence { get; set; }

    /// <summary>
    /// 토큰 사용량
    /// </summary>
    public Usage Usage { get; set; } = new();

    /// <summary>
    /// 요청 ID
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// 총 토큰 수
    /// </summary>
    public uint TotalTokens => Usage.TotalTokens;
}

/// <summary>
/// 출력 콘텐츠 블록 타입
/// </summary>
[JsonConverter(typeof(OutputContentBlockTypeConverter))]
public enum OutputContentBlockType
{
    /// <summary>텍스트</summary>
    Text,
    /// <summary>도구 사용</summary>
    ToolUse,
    /// <summary>생각 과정</summary>
    Thinking,
    /// <summary>검열된 생각</summary>
    RedactedThinking
}

/// <summary>
/// 출력 콘텐츠 블록 타입 JSON 변환기
/// </summary>
public class OutputContentBlockTypeConverter : JsonConverter<OutputContentBlockType>
{
    /// <summary>
    /// JSON에서 enum 값 읽기
    /// </summary>
    public override OutputContentBlockType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() switch
        {
            "text" => OutputContentBlockType.Text,
            "tool_use" => OutputContentBlockType.ToolUse,
            "thinking" => OutputContentBlockType.Thinking,
            "redacted_thinking" => OutputContentBlockType.RedactedThinking,
            _ => OutputContentBlockType.Text
        };
    }

    /// <summary>
    /// enum 값을 JSON으로 쓰기
    /// </summary>
    public override void Write(Utf8JsonWriter writer, OutputContentBlockType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            OutputContentBlockType.Text => "text",
            OutputContentBlockType.ToolUse => "tool_use",
            OutputContentBlockType.Thinking => "thinking",
            OutputContentBlockType.RedactedThinking => "redacted_thinking",
            _ => "text"
        });
    }
}

/// <summary>
/// 출력 콘텐츠 블록
/// </summary>
public class OutputContentBlock
{
    /// <summary>
    /// 블록 타입
    /// </summary>
    public OutputContentBlockType Type { get; set; }

    /// <summary>
    /// 텍스트 내용 (Text 타입일 때)
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// 도구 ID (ToolUse 타입일 때)
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// 도구 이름 (ToolUse 타입일 때)
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 도구 입력 (ToolUse 타입일 때)
    /// </summary>
    public object? Input { get; set; }

    /// <summary>
    /// 생각 과정 (Thinking 타입일 때)
    /// </summary>
    public string? Thinking { get; set; }

    /// <summary>
    /// 서명 (Thinking 타입일 때)
    /// </summary>
    public string? Signature { get; set; }

    /// <summary>
    /// 데이터 (RedactedThinking 타입일 때)
    /// </summary>
    public object? Data { get; set; }
}

/// <summary>
/// 토큰 사용량
/// </summary>
public class Usage
{
    /// <summary>
    /// 입력 토큰 수
    /// </summary>
    public uint InputTokens { get; set; }

    /// <summary>
    /// 캐시 생성 입력 토큰 수
    /// </summary>
    public uint CacheCreationInputTokens { get; set; }

    /// <summary>
    /// 캐시 읽기 입력 토큰 수
    /// </summary>
    public uint CacheReadInputTokens { get; set; }

    /// <summary>
    /// 출력 토큰 수
    /// </summary>
    public uint OutputTokens { get; set; }

    /// <summary>
    /// 총 토큰 수
    /// </summary>
    public uint TotalTokens => InputTokens + OutputTokens + CacheCreationInputTokens + CacheReadInputTokens;
}

