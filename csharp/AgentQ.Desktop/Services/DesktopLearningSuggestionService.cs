using AgentQ.Desktop.ViewModels;

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

        if (analysis.VerificationCommands.Count > 0)
        {
            lessons.Add(CreateLesson(
                "Workspace verification commands",
                $"Use these detected verification commands for this workspace: {string.Join("; ", analysis.VerificationCommands.Take(4))}.",
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
            lessons.Add(CreateLesson(
                $"Failure pattern: {failedStep.Title}",
                string.IsNullOrWhiteSpace(failedStep.Detail)
                    ? $"A previous run failed at: {failedStep.Title}."
                    : $"A previous run failed at: {failedStep.Title}. Detail: {Trim(failedStep.Detail, 180)}",
                ["failure", "desktop"],
                "run failure"));
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

    private static ProjectMemoryLesson CreateLesson(
        string title,
        string content,
        IEnumerable<string> tags,
        string source)
    {
        return new ProjectMemoryLesson
        {
            Id = CreateId(title, content),
            Title = title,
            Content = content,
            Tags = tags.ToList(),
            Confidence = 0.75,
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
}
