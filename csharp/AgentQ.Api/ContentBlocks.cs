using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentQ.Api;

/// <summary>
/// 콘텐츠 블록 타입
/// </summary>
[JsonConverter(typeof(ContentBlockTypeConverter))]
public enum ContentBlockType
{
    /// <summary>텍스트</summary>
    Text,
    /// <summary>도구 사용</summary>
    ToolUse,
    /// <summary>도구 결과</summary>
    ToolResult
}

/// <summary>
/// 콘텐츠 블록 타입 JSON 변환기
/// </summary>
public class ContentBlockTypeConverter : JsonConverter<ContentBlockType>
{
    /// <summary>
    /// JSON에서 enum 값 읽기
    /// </summary>
    public override ContentBlockType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() switch
        {
            "text" => ContentBlockType.Text,
            "tool_use" => ContentBlockType.ToolUse,
            "tool_result" => ContentBlockType.ToolResult,
            _ => ContentBlockType.Text
        };
    }

    /// <summary>
    /// enum 값을 JSON으로 쓰기
    /// </summary>
    public override void Write(Utf8JsonWriter writer, ContentBlockType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ContentBlockType.Text => "text",
            ContentBlockType.ToolUse => "tool_use",
            ContentBlockType.ToolResult => "tool_result",
            _ => "text"
        });
    }
}

/// <summary>
/// 입력 콘텐츠 블록
/// </summary>
public class InputContentBlock
{
    /// <summary>
    /// 블록 타입
    /// </summary>
    public ContentBlockType Type { get; set; }

    /// <summary>
    /// 인덱스
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 텍스트 내용 (Text 타입일 때)
    /// </summary>
    [JsonPropertyName("text")]
    public string? TextContent { get; set; }

    /// <summary>
    /// ID (ToolUse 타입일 때)
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// 이름 (ToolUse 타입일 때)
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 입력 (ToolUse 타입일 때)
    /// </summary>
    [JsonPropertyName("input")]
    public object? Input { get; set; }

    /// <summary>
    /// 도구 사용 ID (ToolResult 타입일 때)
    /// </summary>
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    /// <summary>
    /// 내용 (ToolResult 타입일 때)
    /// </summary>
    [JsonPropertyName("content")]
    public List<ToolResultContentItem>? Content { get; set; }

    /// <summary>
    /// 오류 여부 (ToolResult 타입일 때)
    /// </summary>
    [JsonPropertyName("is_error")]
    public bool IsError { get; set; }

    /// <summary>
    /// 텍스트 블록 생성
    /// </summary>
    /// <param name="text">텍스트 내용</param>
    /// <returns>텍스트 블록</returns>
    public static InputContentBlock CreateText(string text) =>
        new() { Type = ContentBlockType.Text, TextContent = text };

    /// <summary>
    /// 도구 사용 블록 생성
    /// </summary>
    /// <param name="id">도구 ID</param>
    /// <param name="name">도구 이름</param>
    /// <param name="input">도구 입력</param>
    /// <returns>도구 사용 블록</returns>
    public static InputContentBlock CreateToolUse(string id, string name, object input) =>
        new() { Type = ContentBlockType.ToolUse, Id = id, Name = name, Input = input };

    /// <summary>
    /// 도구 결과 블록 생성
    /// </summary>
    /// <param name="toolUseId">도구 사용 ID</param>
    /// <param name="content">결과 내용</param>
    /// <param name="isError">오류 여부</param>
    /// <returns>도구 결과 블록</returns>
    public static InputContentBlock CreateToolResult(string toolUseId, string content, bool isError) =>
        new()
        {
            Type = ContentBlockType.ToolResult,
            ToolUseId = toolUseId,
            Content = new List<ToolResultContentItem> { ToolResultContentItem.CreateText(content) },
            IsError = isError
        };
}

/// <summary>
/// 도구 결과 콘텐츠 항목
/// </summary>
public class ToolResultContentItem
{
    /// <summary>
    /// 콘텐츠 타입
    /// </summary>
    public string ContentType { get; set; } = "text";

    /// <summary>
    /// 텍스트 내용
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// JSON 값
    /// </summary>
    public object? JsonValue { get; set; }

    /// <summary>
    /// 텍스트 항목 생성
    /// </summary>
    /// <param name="text">텍스트 내용</param>
    /// <returns>텍스트 항목</returns>
    public static ToolResultContentItem CreateText(string text) =>
        new() { ContentType = "text", Text = text };

    /// <summary>
    /// JSON 항목 생성
    /// </summary>
    /// <param name="value">JSON 값</param>
    /// <returns>JSON 항목</returns>
    public static ToolResultContentItem CreateJson(object value) =>
        new() { ContentType = "json", JsonValue = value };
}

