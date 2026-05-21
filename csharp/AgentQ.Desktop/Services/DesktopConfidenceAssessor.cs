namespace AgentQ.Desktop.Services;

public static class DesktopConfidenceAssessor
{
    public static DesktopConfidenceAssessment Assess(
        string responseText,
        int toolCallCount,
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<string> executedCommands,
        IReadOnlyList<AgentVerificationPlan> verificationPlans,
        int touchedMemoryCount)
    {
        var score = 55;
        var signals = new List<string>();
        var warnings = new List<string>();

        if (toolCallCount > 0)
        {
            score += Math.Min(20, toolCallCount * 4);
            signals.Add($"{toolCallCount} tool call(s) used as evidence");
        }
        else
        {
            warnings.Add("No tool evidence was gathered");
            score -= 15;
        }

        if (touchedMemoryCount > 0)
        {
            score += 5;
            signals.Add("project memory matched the request");
        }

        if (fileChanges.Count > 0)
        {
            signals.Add($"{fileChanges.Count} file change(s) recorded");
        }

        if (HasVerificationCommand(executedCommands))
        {
            score += 20;
            signals.Add("build or test verification ran");
        }
        else if (fileChanges.Count > 0)
        {
            var alreadySatisfied = verificationPlans.Any(plan => plan.AlreadySatisfied);
            if (!alreadySatisfied)
            {
                score -= 15;
                warnings.Add("Changes were made without a completed build/test command");
            }
        }

        if (verificationPlans.Any(plan => !plan.AlreadySatisfied && !string.IsNullOrWhiteSpace(plan.Command)))
        {
            warnings.Add("Verification is suggested before treating the result as final");
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            score -= 20;
            warnings.Add("Assistant response was empty");
        }

        score = Math.Clamp(score, 0, 100);
        var level = score >= 80 ? "High" : score >= 55 ? "Medium" : "Low";

        return new DesktopConfidenceAssessment
        {
            Score = score,
            Level = level,
            Signals = signals,
            Warnings = warnings
        };
    }

    private static bool HasVerificationCommand(IReadOnlyList<string> commands)
    {
        return commands.Any(command =>
        {
            var normalized = command.Replace('/', '\\').ToLowerInvariant();
            return normalized.Contains("test.cmd", StringComparison.Ordinal) ||
                   normalized.Contains("build.cmd", StringComparison.Ordinal) ||
                   normalized.Contains("build.desktop.cmd", StringComparison.Ordinal) ||
                   normalized.Contains("dotnet test", StringComparison.Ordinal) ||
                   normalized.Contains("dotnet build", StringComparison.Ordinal) ||
                   normalized.Contains("npm test", StringComparison.Ordinal) ||
                   normalized.Contains("npm run build", StringComparison.Ordinal) ||
                   normalized.Contains("pnpm test", StringComparison.Ordinal) ||
                   normalized.Contains("pnpm build", StringComparison.Ordinal);
        });
    }
}
