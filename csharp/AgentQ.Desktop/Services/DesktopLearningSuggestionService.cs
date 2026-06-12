using AgentQ.Desktop.ViewModels;
using System.Text.RegularExpressions;
using System;

namespace AgentQ.Desktop.Services;

public sealed class DesktopLearningSuggestionService
{
    public IReadOnlyList<ProjectMemoryLesson> SuggestWorkspaceLessons(WorkspaceAnalysis analysis)
    {
        var lessons = new List<ProjectMemoryLesson>();

        if (!analysis.ProjectType.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            !analysis.Framework.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            analysis.ProjectMap.Count > 0)
        {
            var projectMap = analysis.ProjectMap.Count == 0
                ? "No clear project map folders detected yet."
                : string.Join("; ", analysis.ProjectMap.Take(5));
            lessons.Add(CreateLesson(
                $"Project profile: {analysis.ProjectType}",
                $"This workspace appears to be {analysis.ProjectType} using {analysis.Framework}. Key areas: {projectMap}",
                ["workspace", "project-map", "profile"],
                "workspace analysis"));
        }

        var verificationCommands = analysis.VerificationCommands
            .Where(command => VerificationCommandPolicy.IsAllowed(command))
            .Take(4)
            .ToList();
        if (verificationCommands.Count > 0)
        {
            lessons.Add(CreateLesson(
                "Workspace verification commands",
                $"Use these detected verification commands for this workspace: {string.Join("; ", verificationCommands)}.",
                ["workspace", "verification", "command"],
                "workspace analysis"));
        }

        if (analysis.KeyFiles.Count > 0)
        {
            lessons.Add(CreateLesson(
                "Workspace key files",
                $"Important files detected in this workspace: {string.Join(", ", analysis.KeyFiles.Take(8))}.",
                ["workspace", "key-files"],
                "workspace analysis"));
        }

        return lessons
            .GroupBy(lesson => lesson.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(3)
            .ToList();
    }

    public IReadOnlyList<ProjectMemoryLesson> SuggestLessons(
        string prompt,
        string response,
        MainViewModel viewModel)
    {
        var lessons = new List<ProjectMemoryLesson>();

        if (response.Contains("Stopped after reaching the maximum tool steps", StringComparison.OrdinalIgnoreCase))
        {
            lessons.Add(CreateLesson(
                "Continue step-limited runs",
                "When a run stops at the maximum tool steps, use Continue instead of restarting the same request.",
                ["desktop", "continuation"],
                "tool step limit"));
        }

        var failedStep = viewModel.RunSteps.LastOrDefault(step => step.State == AgentRunState.Failed);
        if (failedStep != null && !string.IsNullOrWhiteSpace(failedStep.Title))
        {
            lessons.Add(CreateFailureLesson(
                failedStep.Title,
                failedStep.Detail,
                viewModel.Provider,
                viewModel.Model,
                "run failure"));
        }

        var failedVerification = viewModel.VerificationResults.FirstOrDefault(result =>
            string.Equals(result.Status, "FAILED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.Status, "WARNING", StringComparison.OrdinalIgnoreCase));
        if (failedVerification != null)
        {
            lessons.Add(CreateFailureLesson(
                $"Verification failed: {failedVerification.Title}",
                string.IsNullOrWhiteSpace(failedVerification.Command)
                    ? failedVerification.Detail
                    : $"{failedVerification.Detail} Command: {failedVerification.Command}. Output: {failedVerification.OutputPreview}",
                viewModel.Provider,
                viewModel.Model,
                "verification failure"));
        }

        var succeededVerification = viewModel.VerificationResults.FirstOrDefault(result =>
            string.Equals(result.Status, "Passed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase));
        if (succeededVerification != null && !string.IsNullOrWhiteSpace(succeededVerification.Command))
        {
            lessons.Add(CreateLesson(
                $"Verification works: {succeededVerification.Title}",
                $"Use `{succeededVerification.Command}` to verify related changes in this workspace.",
                ["verification", "command"],
                "verification result"));
        }

        if (viewModel.FileChanges.Count > 0 && !string.IsNullOrWhiteSpace(prompt))
        {
            lessons.Add(CreateLesson(
                $"Task pattern: {Trim(prompt, 48)}",
                $"This task changed {viewModel.FileChanges.Count} file(s). For similar work, inspect the Git diff and run focused verification before committing.",
                ["workflow", "git"],
                "completed run"));
        }

        return lessons
            .GroupBy(lesson => lesson.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(3)
            .ToList();
    }

    public ProjectMemoryLesson CreateFailureLesson(
        string title,
        string? detail,
        string provider,
        string model,
        string source)
    {
        var tags = ClassifyFailureTags(title, detail);
        var providerText = string.IsNullOrWhiteSpace(provider) ? "unknown provider" : provider;
        var modelText = string.IsNullOrWhiteSpace(model) ? "unknown model" : model;
        var detailText = string.IsNullOrWhiteSpace(detail)
            ? "No detail was captured."
            : Trim(RedactSensitiveText(detail), 220);

        return CreateLesson(
            $"Failure pattern: {Trim(RedactSensitiveText(title), 72)}",
            $"A previous failure happened with {RedactSensitiveText(providerText)}/{RedactSensitiveText(modelText)}. Detail: {detailText}",
            tags,
            RedactSensitiveText(source),
            FailureFingerprintService.Create(title, detailText));
    }

    private static ProjectMemoryLesson CreateLesson(
        string title,
        string content,
        IEnumerable<string> tags,
        string source,
        string failureFingerprint = "")
    {
        return new ProjectMemoryLesson
        {
            Id = CreateId(title, content),
            Title = title,
            Content = content,
            Tags = tags.ToList(),
            Confidence = 0.75,
            FailureFingerprint = failureFingerprint,
            CreatedAt = DateTime.Now,
            Source = source
        };
    }

    private static string CreateId(string title, string content)
    {
        var seed = $"{title}\n{content}".Trim().ToLowerInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        return $"lesson-{hash[..12]}";
    }

    private static string Trim(string value, int maxLength)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static string RedactSensitiveText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = Regex.Replace(value, @"sk-[A-Za-z0-9_-]{8,}", "[REDACTED]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, @"bearer\s+[A-Za-z0-9._-]{8,}", "Bearer [REDACTED]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, @"(?i)(api[-_\s]?key|access[-_\s]?token|refresh[-_\s]?token|private[-_\s]?key|password|secret)\s*[:=]\s*\S+", "$1=[REDACTED]");
        redacted = Regex.Replace(redacted, @"postgres(?:ql)?://[^@\s]+:[^@\s]+@", "postgres://[REDACTED]@", RegexOptions.IgnoreCase);
        return redacted;
    }

    private static IReadOnlyList<string> ClassifyFailureTags(string title, string? detail)
    {
        var text = $"{title} {detail}".ToLowerInvariant();
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "failure",
            "error-history"
        };

        if (text.Contains("embedding"))
        {
            tags.Add("embedding");
        }

        if (text.Contains("provider") ||
            text.Contains("model") ||
            text.Contains("api") ||
            text.Contains("400") ||
            text.Contains("404") ||
            text.Contains("request failed"))
        {
            tags.Add("provider");
        }

        if (text.Contains("verification") ||
            text.Contains("test") ||
            text.Contains("build") ||
            text.Contains("exit code"))
        {
            tags.Add("verification");
        }

        if (text.Contains("tool failed") || text.Contains("tool error"))
        {
            tags.Add("tool");
        }

        if (text.Contains("cancelled") || text.Contains("timed out") || text.Contains("timeout"))
        {
            tags.Add("timeout");
        }

        if (text.Contains("permission") || text.Contains("denied") || text.Contains("blocked"))
        {
            tags.Add("permission");
        }

        return tags.ToList();
    }
}
