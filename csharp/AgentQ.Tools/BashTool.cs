using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AgentQ.Tools;

/// <summary>
/// Bash 명령 실행 도구
/// </summary>
public class BashTool : ITool
{
    /// <summary>
    /// 도구 이름
    /// </summary>
    public string Name => "bash";

    /// <summary>
    /// 도구 설명
    /// </summary>
    public string Description => "Execute a bash command and return its output";

    /// <summary>
    /// 권한 확인 필요 여부
    /// </summary>
    public bool RequiresPermission => true;

    /// <summary>
    /// 입력 스키마 (JSON Schema)
    /// </summary>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "The bash command to execute" },
            timeout = new { type = "integer", description = "Timeout in milliseconds (default 30000)" }
        },
        required = new[] { "command" }
    };

    /// <summary>
    /// 도구 실행
    /// </summary>
    /// <param name="input">입력 파라미터</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>도구 실행 결과</returns>
    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!input.TryGetValue("command", out var cmdObj) || cmdObj is not string command)
            return ToolResult.Error("Missing required parameter: command");

        var timeout = 30000;
        if (TryGetInt32(input, "timeout", out var parsedTimeout)) timeout = parsedTimeout;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash",
                Arguments = OperatingSystem.IsWindows()
                    ? $"-NoProfile -Command \"{command}\""
                    : $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                return ToolResult.Error($"Command timed out after {timeout}ms: {command}");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var output = new Dictionary<string, object?>
            {
                ["exitCode"] = process.ExitCode,
                ["stdout"] = stdout,
                ["stderr"] = stderr
            };

            return ToolResult.Success(JsonSerializer.Serialize(output));
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to execute command: {ex.Message}");
        }
    }

    /// <summary>
    /// Int32 값 파싱 시도
    /// </summary>
    /// <param name="input">입력 딕셔너리</param>
    /// <param name="key">키</param>
    /// <param name="value">파싱된 값 (out)</param>
    /// <returns>파싱 성공 여부</returns>
    private static bool TryGetInt32(Dictionary<string, object?> input, string key, out int value)
    {
        value = 0;
        if (!input.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is int intValue)
        {
            value = intValue;
            return true;
        }

        if (rawValue is long longValue && longValue is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)longValue;
            return true;
        }

        if (rawValue is string stringValue && int.TryParse(stringValue, out var parsed))
        {
            value = parsed;
            return true;
        }

        if (rawValue is JsonElement json && json.TryGetInt32(out parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}

