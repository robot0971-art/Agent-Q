namespace AgentQ.Tools;

/// <summary>
/// 도구 인터페이스
/// </summary>
public interface ITool
{
    /// <summary>
    /// 도구 이름
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 도구 설명
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 입력 스키마 (JSON Schema)
    /// </summary>
    object InputSchema { get; }

    /// <summary>
    /// 권한 확인 필요 여부
    /// </summary>
    bool RequiresPermission { get; }

    /// <summary>
    /// 도구 실행
    /// </summary>
    /// <param name="input">입력 파라미터</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>도구 실행 결과</returns>
    Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default);
}

/// <summary>
/// 도구 실행 결과
/// </summary>
public class ToolResult
{
    /// <summary>
    /// 결과 내용
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 오류 발생 여부
    /// </summary>
    public bool IsError { get; init; }

    /// <summary>
    /// 오류 메시지
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 성공 결과 생성
    /// </summary>
    /// <param name="content">결과 내용</param>
    /// <returns>성공 도구 결과</returns>
    public static ToolResult Success(string content) => new() { Content = content };

    /// <summary>
    /// 오류 결과 생성
    /// </summary>
    /// <param name="message">오류 메시지</param>
    /// <returns>오류 도구 결과</returns>
    public static ToolResult Error(string message) => new() { Content = message, IsError = true, ErrorMessage = message };
}

