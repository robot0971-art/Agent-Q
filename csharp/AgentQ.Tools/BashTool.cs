using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentQ.Tools;

/// <summary>
/// Executes a shell command and returns its output.
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
        (new Regex(@"(^|\s)(del|erase)\b(?=.*(?:/s|-recurse\b))(?=.*(?:/q|/f|-force\b))", RegexOptions.IgnoreCase | RegexOptions.Compiled), "destructive delete"),
        (new Regex(@"(^|\s)(rmdir|rd)\b(?=.*(?:/s|-recurse\b))(?=.*(?:/q|-force\b))", RegexOptions.IgnoreCase | RegexOptions.Compiled), "recursive directory delete"),
        (new Regex(@"\b(remove-item|ri|rm|del|erase)\b(?=.*(?:-r\b|-recurse\b))(?=.*(?:-fo\b|-force\b))", RegexOptions.IgnoreCase | RegexOptions.Compiled), "recursive forced delete"),
        (new Regex(@"\b(encodedcommand|enc)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "encoded command execution"),
        (new Regex(@"(^|\s)diskpart(\.exe)?(\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "disk partition management"),
        (new Regex(@"(^|\s)fsutil(\.exe)?\s+file\s+setzerodata\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "file zeroing"),
        (new Regex(@"(^|\s)cipher(\.exe)?\s+/w\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "disk wipe"),
        (new Regex(@"(^|\s)net(\.exe)?\s+user\b.*\s+/delete\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "user account deletion"),
        (new Regex(@"(^|\s)takeown(\.exe)?\b.*\b(icacls|del|erase|rmdir|rd|remove-item|ri|rm)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ownership takeover followed by destructive command"),
        (new Regex(@"(^|\s)icacls(\.exe)?\b.*\s/grant\b.*\b(del|erase|rmdir|rd|remove-item|ri|rm)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "permission change followed by destructive command")
    ];

    /// <summary>
    /// Tool name.
    /// </summary>
    public string Name => "bash";

    /// <summary>
    /// Tool description.
    /// </summary>
    public string Description => "Execute a shell command and return its output";

    /// <summary>
    /// Whether this tool requires user permission.
    /// </summary>
    public bool RequiresPermission => true;

    /// <summary>
    /// Input schema.
    /// </summary>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "The shell command to execute" },
            timeout = new { type = "integer", description = "Timeout in milliseconds (1000-120000, default 30000)" }
        },
        required = new[] { "command" }
    };

    /// <summary>
    /// Executes the tool.
    /// </summary>
    /// <param name="input">Input parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tool execution result.</returns>
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
            var encoding = Encoding.UTF8;
            #pragma warning disable CA1416
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    encoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
                }
                catch
                {
                    encoding = Encoding.UTF8;
                }
            }
            #pragma warning restore CA1416

            var psi = new ProcessStartInfo
            {
                // Use PowerShell on Windows and bash elsewhere.
                FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
                WorkingDirectory = ResolveWorkingDirectory()
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
    /// Tries to read an input value as an Int32.
    /// </summary>
    /// <param name="input">Input dictionary.</param>
    /// <param name="key">Key to read.</param>
    /// <param name="value">Parsed value.</param>
    /// <returns>Whether parsing succeeded.</returns>
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

    private static string ResolveWorkingDirectory()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT");
        return !string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot)
            ? workspaceRoot
            : Environment.CurrentDirectory;
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
