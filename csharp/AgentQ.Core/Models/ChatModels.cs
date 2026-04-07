using System.Text.Json.Serialization;

namespace AgentQ.Core.Models;

/// <summary>
/// 채팅 역할 열거형
/// </summary>
public enum ChatRole
{
    /// <summary>시스템</summary>
    System,
    /// <summary>사용자</summary>
    User,
    /// <summary>어시스턴트</summary>
    Assistant,
    /// <summary>도구</summary>
    Tool
}

/// <summary>
/// 채팅 메시지
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// 메시지 역할
    /// </summary>
    public ChatRole Role { get; set; }

    /// <summary>
    /// 메시지 내용 목록
    /// </summary>
    public List<ChatContent> Content { get; set; } = new();

    /// <summary>
    /// 시스템 텍스트 메시지 생성
    /// </summary>
    /// <param name="text">텍스트 내용</param>
    /// <returns>시스템 메시지</returns>
    public static ChatMessage SystemText(string text) =>
        new() { Role = ChatRole.System, Content = new() { ChatContent.CreateText(text) } };

    /// <summary>
    /// 사용자 텍스트 메시지 생성
    /// </summary>
    /// <param name="text">텍스트 내용</param>
    /// <returns>사용자 메시지</returns>
    public static ChatMessage UserText(string text) =>
        new() { Role = ChatRole.User, Content = new() { ChatContent.CreateText(text) } };

    /// <summary>
    /// 어시스턴트 텍스트 메시지 생성
    /// </summary>
    /// <param name="text">텍스트 내용</param>
    /// <returns>어시스턴트 메시지</returns>
    public static ChatMessage AssistantText(string text) =>
        new() { Role = ChatRole.Assistant, Content = new() { ChatContent.CreateText(text) } };

    /// <summary>
    /// 어시스턴트 도구 사용 메시지 생성
    /// </summary>
    /// <param name="toolId">도구 ID</param>
    /// <param name="toolName">도구 이름</param>
    /// <param name="input">도구 입력</param>
    /// <returns>도구 사용 메시지</returns>
    public static ChatMessage AssistantToolUse(string toolId, string toolName, object input) =>
        new() { Role = ChatRole.Assistant, Content = new() { ChatContent.CreateToolUse(toolId, toolName, input) } };

    /// <summary>
    /// 사용자 도구 결과 메시지 생성
    /// </summary>
    /// <param name="toolUseId">도구 사용 ID</param>
    /// <param name="result">결과 내용</param>
    /// <param name="isError">오류 여부</param>
    /// <returns>도구 결과 메시지</returns>
    public static ChatMessage UserToolResult(string toolUseId, string result, bool isError) =>
        new() { Role = ChatRole.User, Content = new() { ChatContent.CreateToolResult(toolUseId, result, isError) } };
}

/// <summary>
/// 콘텐츠 타입 열거형
/// </summary>
public enum ContentType
{
    /// <summary>텍스트</summary>
    Text,
    /// <summary>도구 사용</summary>
    ToolUse,
    /// <summary>도구 결과</summary>
    ToolResult
}

/// <summary>
/// 채팅 콘텐츠
/// </summary>
public class ChatContent
{
    /// <summary>
    /// 콘텐츠 타입
    /// </summary>
    public ContentType Type { get; set; }

    /// <summary>
    /// 텍스트 내용 (Text 타입일 때)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>
    /// 도구 ID (ToolUse 타입일 때)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolId { get; set; }

    /// <summary>
    /// 도구 이름 (ToolUse 타입일 때)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; set; }

    /// <summary>
    /// 도구 입력 (ToolUse 타입일 때)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ToolInput { get; set; }

    /// <summary>
    /// 도구 사용 ID (ToolResult 타입일 때)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; set; }

    /// <summary>
    /// 도구 결과 (ToolResult 타입일 때)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolResult { get; set; }

    /// <summary>
    /// 도구 오류 여부 (ToolResult 타입일 때)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsToolError { get; set; }

    /// <summary>
    /// 텍스트 콘텐츠 생성
    /// </summary>
    /// <param name="text">텍스트 내용</param>
    /// <returns>텍스트 콘텐츠</returns>
    public static ChatContent CreateText(string text) =>
        new() { Type = ContentType.Text, Text = text };

    /// <summary>
    /// 도구 사용 콘텐츠 생성
    /// </summary>
    /// <param name="toolId">도구 ID</param>
    /// <param name="toolName">도구 이름</param>
    /// <param name="input">도구 입력</param>
    /// <returns>도구 사용 콘텐츠</returns>
    public static ChatContent CreateToolUse(string toolId, string toolName, object input) =>
        new() { Type = ContentType.ToolUse, ToolId = toolId, ToolName = toolName, ToolInput = input };

    /// <summary>
    /// 도구 결과 콘텐츠 생성
    /// </summary>
    /// <param name="toolUseId">도구 사용 ID</param>
    /// <param name="result">결과 내용</param>
    /// <param name="isError">오류 여부</param>
    /// <returns>도구 결과 콘텐츠</returns>
    public static ChatContent CreateToolResult(string toolUseId, string result, bool isError) =>
        new() { Type = ContentType.ToolResult, ToolUseId = toolUseId, ToolResult = result, IsToolError = isError };
}

/// <summary>
/// 채팅 컨텍스트
/// </summary>
public class ChatContext
{
    /// <summary>
    /// 사용할 모델
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 메시지 목록
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>
    /// 시스템 프롬프트
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// 최대 토큰 수
    /// </summary>
    public uint MaxTokens { get; set; } = 1024;

    /// <summary>
    /// 스트리밍 사용 여부
    /// </summary>
    public bool Stream { get; set; } = true;

    /// <summary>
    /// 단일 대화 턴에서 허용되는 최대 tool loop 횟수
    /// </summary>
    public int MaxSteps { get; set; } = 8;
}

/// <summary>
/// 채팅 응답
/// </summary>
public class ChatResponse
{
    /// <summary>
    /// 응답 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 사용된 모델
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 응답 내용 목록
    /// </summary>
    public List<ChatContent> Content { get; set; } = new();

    /// <summary>
    /// 토큰 사용량
    /// </summary>
    public UsageStats? Usage { get; set; }
}

/// <summary>
/// 토큰 사용량 통계
/// </summary>
public class UsageStats
{
    /// <summary>
    /// 입력 토큰 수
    /// </summary>
    public int InputTokens { get; set; }

    /// <summary>
    /// 출력 토큰 수
    /// </summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// 총 토큰 수
    /// </summary>
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// 스트리밍 청크
/// </summary>
public class StreamChunk
{
    /// <summary>
    /// 텍스트 델타 (스트리밍 중인 텍스트)
    /// </summary>
    public string? TextDelta { get; set; }

    /// <summary>
    /// 도구 사용 델타
    /// </summary>
    public ToolUseChunk? ToolUseDelta { get; set; }

    /// <summary>
    /// 스트리밍 완료 여부
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// 토큰 사용량
    /// </summary>
    public UsageStats? Usage { get; set; }
}

/// <summary>
/// 도구 사용 청크
/// </summary>
public class ToolUseChunk
{
    /// <summary>
    /// 도구 ID
    /// </summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// 도구 이름
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// 부분 입력 (스트리밍 중)
    /// </summary>
    public string? PartialInput { get; set; }

    /// <summary>
    /// 완료 여부
    /// </summary>
    public bool IsComplete { get; set; }
}

/// <summary>
/// 도구 정의
/// </summary>
public class ToolDefinition
{
    /// <summary>
    /// 도구 이름
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 도구 설명
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 입력 스키마 (JSON Schema)
    /// </summary>
    public object InputSchema { get; set; } = new();
}

