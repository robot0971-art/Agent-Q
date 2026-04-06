using System.Text.Json;

namespace AgentQ.Tools;

/// <summary>
/// 플러그인 에코 도구 (테스트용)
/// </summary>
public class PluginEchoTool : ITool
{
    /// <summary>
    /// 도구 이름
    /// </summary>
    public string Name => "plugin_echo";

    /// <summary>
    /// 도구 설명
    /// </summary>
    public string Description => "Echo plugin-style input for parity testing";

    /// <summary>
    /// 권한 확인 필요 여부
    /// </summary>
    public bool RequiresPermission => false;

    /// <summary>
    /// 입력 스키마 (JSON Schema)
    /// </summary>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            message = new { type = "string", description = "Message to echo back" }
        },
        required = new[] { "message" }
    };

    /// <summary>
    /// 도구 실행
    /// </summary>
    /// <param name="input">입력 파라미터</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>도구 실행 결과</returns>
    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!input.TryGetValue("message", out var messageObj) || messageObj is not string message)
        {
            return Task.FromResult(ToolResult.Error("Missing required parameter: message"));
        }

        var output = new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?>
            {
                ["message"] = message
            },
            ["message"] = message
        };

        return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(output)));
    }
}

