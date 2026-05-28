using System.Text.Json;

namespace AgentQ.Desktop.Services;

public static class ShellVerificationResultDetector
{
    private static readonly string[] VerificationCommandMarkers =
    [
        "dotnet test",
        "dotnet build",
        "npm test",
        "npm run test",
        "npm run build",
        "npm run lint",
        "pnpm test",
        "pnpm build",
        "pnpm lint",
        "yarn test",
        "yarn build",
        "yarn lint"
    ];

    private static readonly string[] SuccessOutputMarkers =
    [
        "passed!",
        "test run successful",
        "build succeeded",
        "compiled successfully",
        "built in",
        "0 error",
        "0 errors",
        "\uD1B5\uACFC!",
        "\uBE4C\uB4DC\uD588\uC2B5\uB2C8\uB2E4",
        "\uC624\uB958 0\uAC1C",
        "0 failed",
        "failed:     0",
        "\uC2E4\uD328:     0",
        "failures: 0"
    ];

    public static bool TryCreate(
        string toolName,
        IReadOnlyDictionary<string, object?> input,
        string resultContent,
        out VerificationResultCard result)
    {
        result = null!;
        if (!string.Equals(toolName, "bash", StringComparison.Ordinal) ||
            !TryGetString(input, "command", out var command) ||
            !LooksLikeVerificationCommand(command) ||
            !TryParseShellResult(resultContent, out var exitCode, out var stdout, out var stderr) ||
            exitCode != 0)
        {
            return false;
        }

        var combinedOutput = string.Join(Environment.NewLine, stdout, stderr).Trim();
        if (!LooksLikeSuccessfulVerificationOutput(combinedOutput))
        {
            return false;
        }

        var plan = new AgentVerificationPlan
        {
            Title = BuildTitle(command),
            Command = command,
            Reason = "Shell command completed successfully during the agent run."
        };
        var runResult = new VerificationRunResult
        {
            ExitCode = exitCode,
            StandardOutput = stdout,
            StandardError = stderr
        };

        result = VerificationResultCard.Passed(
            plan,
            runResult,
            "Shell verification passed during the agent run.");
        return true;
    }

    private static bool LooksLikeVerificationCommand(string command)
    {
        return VerificationCommandMarkers.Any(marker =>
            command.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeSuccessfulVerificationOutput(string output)
    {
        return SuccessOutputMarkers.Any(marker =>
            output.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildTitle(string command)
    {
        if (command.Contains("dotnet test", StringComparison.OrdinalIgnoreCase))
        {
            return "dotnet test";
        }

        if (command.Contains("dotnet build", StringComparison.OrdinalIgnoreCase))
        {
            return "dotnet build";
        }

        if (command.Contains("npm run build", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("pnpm build", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("yarn build", StringComparison.OrdinalIgnoreCase))
        {
            return "frontend build";
        }

        if (command.Contains("lint", StringComparison.OrdinalIgnoreCase))
        {
            return "lint";
        }

        return "shell verification";
    }

    private static bool TryParseShellResult(
        string resultContent,
        out int exitCode,
        out string stdout,
        out string stderr)
    {
        exitCode = -1;
        stdout = string.Empty;
        stderr = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(resultContent);
            var root = document.RootElement;
            if (!root.TryGetProperty("exitCode", out var exitCodeElement) ||
                !exitCodeElement.TryGetInt32(out exitCode))
            {
                return false;
            }

            stdout = root.TryGetProperty("stdout", out var stdoutElement)
                ? stdoutElement.GetString() ?? string.Empty
                : string.Empty;
            stderr = root.TryGetProperty("stderr", out var stderrElement)
                ? stderrElement.GetString() ?? string.Empty
                : string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object?> input, string key, out string value)
    {
        value = string.Empty;
        if (!input.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is string stringValue)
        {
            value = stringValue;
            return !string.IsNullOrWhiteSpace(value);
        }

        if (rawValue is JsonElement element && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }
}
