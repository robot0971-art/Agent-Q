using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public sealed class VerificationFailureClassifier
{
    public VerificationFailureAnalysis Analyze(AgentVerificationPlan plan, VerificationRunResult result)
    {
        var output = result.CombinedOutput;
        var evidence = ExtractEvidence(output);

        if (Matches(output, "The command is not in the verification allowlist", "not in the verification allowlist"))
        {
            return Create(
                VerificationFailureKind.CommandNotAllowed,
                "Verification command was not allowed",
                "The command failed because it is outside the desktop verification allowlist.",
                "Use an approved verification command or update the verification policy deliberately.",
                evidence);
        }

        if (Matches(output, "timed out", "timeout", "operation canceled"))
        {
            return Create(
                VerificationFailureKind.Timeout,
                "Verification timed out",
                "The verification command exceeded its time limit or was cancelled.",
                "Check for hung tests, long builds, deadlocks, or commands waiting for input.",
                evidence);
        }

        if (Matches(output, "CS\\d{4}", "MSB\\d{4}", "Build FAILED", "error CS", "error MSB"))
        {
            return Create(
                VerificationFailureKind.CompileError,
                "Compilation failed",
                "The output contains compiler or MSBuild errors.",
                "Fix the reported compile errors first, then rerun the same verification.",
                evidence);
        }

        if (Matches(output, "Failed:", "Failed!", "실패:", "Xunit.Sdk", "Assert\\.", "Test Failed", "Total tests:") ||
            Matches(plan.Command ?? string.Empty, "test", "vstest", "dotnet test"))
        {
            return Create(
                VerificationFailureKind.TestFailure,
                "Tests failed",
                "The verification command appears to have run tests and at least one test failed.",
                "Identify the first failing test, inspect the assertion and changed code, then rerun focused tests if possible.",
                evidence);
        }

        if (Matches(output, "Access is denied", "UnauthorizedAccessException", "permission denied", "denied"))
        {
            return Create(
                VerificationFailureKind.PermissionBlocked,
                "Permission blocked verification",
                "The command output indicates a filesystem, process, or policy permission problem.",
                "Confirm whether the command needs broader permission or should be rewritten to stay inside the workspace.",
                evidence);
        }

        if (Matches(output, "not recognized", "command not found", "No such file or directory", "could not find", "Cannot find path"))
        {
            return Create(
                VerificationFailureKind.MissingDependency,
                "Missing command or file",
                "The verification command could not find a required executable, file, or path.",
                "Check project setup, PATH, restore/install steps, and workspace-relative paths.",
                evidence);
        }

        if (Matches(output, "NETSDK", "restore", "NuGet", "assets file", "project.assets.json", "SDK"))
        {
            return Create(
                VerificationFailureKind.EnvironmentIssue,
                "Build environment issue",
                "The output points to SDK, restore, package, or environment setup problems.",
                "Verify SDK version, restore state, package sources, and environment variables before editing product code.",
                evidence);
        }

        return Create(
            VerificationFailureKind.Unknown,
            "Unknown verification failure",
            "The verification command failed, but no known failure pattern matched confidently.",
            "Read the output, inspect relevant files, and classify the cause before editing.",
            evidence);
    }

    public VerificationFailureAnalysis AnalyzeException(Exception ex)
    {
        var message = ex.Message;
        if (ex is TimeoutException || Matches(message, "timed out", "timeout", "cancelled", "canceled"))
        {
            return Create(
                VerificationFailureKind.Timeout,
                ex is OperationCanceledException ? "Verification cancelled" : "Verification timed out",
                message,
                ex is OperationCanceledException
                    ? "No code fix is required unless cancellation was unexpected."
                    : "Check for hung tests, long builds, deadlocks, or commands waiting for input.",
                [message]);
        }

        if (Matches(message, "allowlist", "not allowed"))
        {
            return Create(
                VerificationFailureKind.CommandNotAllowed,
                "Verification command was not allowed",
                message,
                "Use an approved verification command or update the verification policy deliberately.",
                [message]);
        }

        return Create(
            VerificationFailureKind.Unknown,
            "Verification failed before producing a result",
            message,
            "Inspect the exception and command setup before editing code.",
            [message]);
    }

    private static VerificationFailureAnalysis Create(
        VerificationFailureKind kind,
        string title,
        string summary,
        string suggestedNextStep,
        IReadOnlyList<string> evidence)
    {
        return new VerificationFailureAnalysis
        {
            Kind = kind,
            Title = title,
            Summary = summary,
            SuggestedNextStep = suggestedNextStep,
            Evidence = evidence
        };
    }

    private static IReadOnlyList<string> ExtractEvidence(string output)
    {
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Where(line => Matches(line, "error ", "failed", "timeout", "denied", "not found", "CS\\d{4}", "MSB\\d{4}", "Assert\\."))
            .Take(8)
            .ToList();
    }

    private static bool Matches(string value, params string[] patterns)
    {
        return patterns.Any(pattern =>
            Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }
}
