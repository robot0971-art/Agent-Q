using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AgentQ.Tools;

public class BashTool : ITool
{
    public string Name => "bash";
    public string Description => "Execute a bash command and return its output";
    public bool RequiresPermission => true;
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

