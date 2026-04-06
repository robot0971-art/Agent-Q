namespace AgentQ.Api;

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
    public string? Description { get; set; }

    /// <summary>
    /// 입력 스키마 (JSON Schema)
    /// </summary>
    public object InputSchema { get; set; } = new();
}

/// <summary>
/// 도구 선택 방식
/// </summary>
public enum ToolChoiceType
{
    /// <summary>자동 선택</summary>
    Auto,
    /// <summary>모든 도구</summary>
    Any,
    /// <summary>특정 도구</summary>
    Tool
}

/// <summary>
/// 도구 선택
/// </summary>
public class ToolChoice
{
    /// <summary>
    /// 선택 방식
    /// </summary>
    public ToolChoiceType Type { get; set; }

    /// <summary>
    /// 도구 이름 (Tool 방식일 때)
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 자동 선택
    /// </summary>
    public static ToolChoice Auto => new() { Type = ToolChoiceType.Auto };

    /// <summary>
    /// 모든 도구 선택
    /// </summary>
    public static ToolChoice Any => new() { Type = ToolChoiceType.Any };

    /// <summary>
    /// 특정 도구 선택
    /// </summary>
    /// <param name="name">도구 이름</param>
    /// <returns>도구 선택</returns>
    public static ToolChoice Named(string name) => new() { Type = ToolChoiceType.Tool, Name = name };
}

