using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentQ.Tools;

/// <summary>
/// 셸 명령을 실행하고 결과를 반환하는 도구입니다.
/// </summary>
public class BashTool : ITool
{
    private const int DefaultTimeoutMs = 30000;
    private const int MinimumTimeoutMs = 1000;
    private const int MaximumTimeoutMs = 120000;
    private const int MaxOutputLength = 32000;
    private static readonly (Regex Pattern, string Reason)[] BlockedCommandPatterns =
    [
        (new Regex(@"(^|\s)rm\s+-rf\s+(/|\*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "destructive recursive delete"),
        (new Regex(@"(^|\s)(shutdown|reboot)(\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "system shutdown/reboot"),
        (new Regex(@"(^|\s)format(\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "disk format"),
        (new Regex(@"(^|\s)del\s+(/s|/q|/f)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "destructive delete"),
        (new Regex(@"Remove-Item\b.*-Recurse\b.*-Force\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "recursive forced delete")
    ];

    /// <summary>
    /// 도구 이름입니다.
    /// </summary>
    public string Name => "bash";

    /// <summary>
    /// 도구 설명입니다.
    /// </summary>
    public string Description => "Execute a bash command and return its output";

    /// <summary>
    /// 사용자 권한 확인이 필요한지 여부입니다.
    /// </summary>
    public bool RequiresPermission => true;

    /// <summary>
    /// 입력 스키마입니다.
    /// </summary>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "The bash command to execute" },
            timeout = new { type = "integer", description = "Timeout in milliseconds (1000-120000, default 30000)" }
        },
        required = new[] { "command" }
    };

    /// <summary>
    /// 도구를 실행합니다.
    /// </summary>
    /// <param name="input">입력 파라미터</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>도구 실행 결과</returns>
    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!input.TryGetValue("command", out var cmdObj) || cmdObj is not string command)
            return ToolResult.Error("Missing required parameter: command");

        command = command.Trim();
        if (string.IsNullOrWhiteSpace(command))
            return ToolResult.Error("Command cannot be empty");

        if (TryGetBlockedReason(command, out var blockedReason))
            return ToolResult.Error($"Command blocked by safety policy: {blockedReason}");

        var timeout = DefaultTimeoutMs;
        if (TryGetInt32(input, "timeout", out var parsedTimeout))
        {
            if (parsedTimeout < MinimumTimeoutMs || parsedTimeout > MaximumTimeoutMs)
            {
                return ToolResult.Error($"Timeout must be between {MinimumTimeoutMs}ms and {MaximumTimeoutMs}ms");
            }

            timeout = parsedTimeout;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                // Windows에서는 PowerShell, 그 외 환경에서는 bash를 사용합니다.
                FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (OperatingSystem.IsWindows())
            {
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(command);
            }
            else
            {
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(command);
            }

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

            var stdout = Truncate(await stdoutTask, out var stdoutTruncated);
            var stderr = Truncate(await stderrTask, out var stderrTruncated);

            var output = new Dictionary<string, object?>
            {
                ["exitCode"] = process.ExitCode,
                ["stdout"] = stdout,
                ["stderr"] = stderr,
                ["stdoutTruncated"] = stdoutTruncated,
                ["stderrTruncated"] = stderrTruncated,
                ["timeoutMs"] = timeout
            };

            return ToolResult.Success(JsonSerializer.Serialize(output));
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to execute command: {ex.Message}");
        }
    }

    /// <summary>
    /// 지정한 입력 값을 Int32로 변환할 수 있는지 확인합니다.
    /// </summary>
    /// <param name="input">입력 딕셔너리</param>
    /// <param name="key">조회할 키</param>
    /// <param name="value">변환된 값</param>
    /// <returns>변환 성공 여부</returns>
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

    private static bool TryGetBlockedReason(string command, out string reason)
    {
        foreach (var (pattern, blockedReason) in BlockedCommandPatterns)
        {
            if (pattern.IsMatch(command))
            {
                reason = blockedReason;
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private static string Truncate(string value, out bool wasTruncated)
    {
        if (value.Length <= MaxOutputLength)
        {
            wasTruncated = false;
            return value;
        }

        wasTruncated = true;
        return value[..MaxOutputLength] + "\n...[truncated]";
    }
}
