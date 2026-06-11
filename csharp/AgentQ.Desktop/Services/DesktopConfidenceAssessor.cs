namespace AgentQ.Desktop.Services;

public static class DesktopConfidenceAssessor
{
    public static DesktopConfidenceAssessment Assess(
        string responseText,
        int toolCallCount,
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<string> executedCommands,
        IReadOnlyList<AgentVerificationPlan> verificationPlans,
        int touchedMemoryCount,
        IReadOnlyList<ToolReplayEntry>? toolEvidence = null)
    {
        var score = 55;
        var signals = new List<string>();
        var warnings = new List<string>();
        toolEvidence ??= [];

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

        var successfulTools = toolEvidence.Where(entry => !entry.IsError).ToList();
        var searchToolCount = successfulTools.Count(entry => IsSearchTool(entry.ToolName));
        var readFileCount = successfulTools.Count(entry => string.Equals(entry.ToolName, "read_file", StringComparison.OrdinalIgnoreCase));
        var hybridSearch = successfulTools.Where(entry => string.Equals(entry.ToolName, "hybrid_search", StringComparison.OrdinalIgnoreCase)).ToList();

        if (searchToolCount > 0)
        {
            score += Math.Min(10, searchToolCount * 2);
            signals.Add($"{searchToolCount} search/navigation tool(s) gathered context");
        }

        if (readFileCount > 0)
        {
            score += Math.Min(8, readFileCount * 2);
            signals.Add($"{readFileCount} file read(s) inspected concrete context");
        }

        if (hybridSearch.Any(HasGraphSignal))
        {
            score += 6;
            signals.Add("dependency graph evidence contributed to retrieval");
        }

        if (hybridSearch.Any(HasMemorySignal))
        {
            score += 4;
            signals.Add("project memory evidence contributed to retrieval");
        }

        if (hybridSearch.Any(HasGitSignal))
        {
            score += 3;
            signals.Add("Git recency evidence contributed to retrieval");
        }

        if (fileChanges.Count > 0)
        {
            signals.Add($"{fileChanges.Count} file change(s) recorded");
            var onlyNewChanges = fileChanges.All(change => !change.ExistedBefore);
            var onlyDirectoryChanges = fileChanges.All(IsDirectoryChange);

            if (onlyDirectoryChanges)
            {
                score += 12;
                signals.Add("directory creation/deletion evidence matched a simple filesystem task");
            }
            else if (onlyNewChanges)
            {
                score += 6;
                signals.Add("new file creation evidence was recorded");
            }

            if (!onlyNewChanges && toolEvidence.Count > 0 && readFileCount == 0)
            {
                score -= 12;
                warnings.Add("Changes were made without reading file context in this run");
            }

            if (!onlyNewChanges && toolEvidence.Count > 0 && searchToolCount == 0)
            {
                score -= 10;
                warnings.Add("Changes were made without search or symbol navigation evidence");
            }
        }

        if (HasVerificationCommand(executedCommands))
        {
            score += 20;
            signals.Add("build or test verification ran");
        }
        else if (fileChanges.Count > 0)
        {
            var alreadySatisfied = verificationPlans.Any(plan => plan.AlreadySatisfied);
            var hasAutomatedVerificationCommand = verificationPlans.Any(plan => !string.IsNullOrWhiteSpace(plan.Command));
            if (!alreadySatisfied && hasAutomatedVerificationCommand)
            {
                score -= 15;
                warnings.Add("Changes were made without a completed build/test command");
            }
        }

        if (verificationPlans.Any(plan => !plan.AlreadySatisfied && !string.IsNullOrWhiteSpace(plan.Command)))
        {
            warnings.Add("Verification is suggested before treating the result as final");
        }

        if (toolEvidence.Count > 0 && successfulTools.Count == 0)
        {
            score -= 10;
            warnings.Add("All recorded tool evidence failed");
        }

        if (toolEvidence.Count > 0 && toolEvidence.Count(entry => entry.IsError) >= Math.Max(2, toolEvidence.Count / 2))
        {
            score -= 5;
            warnings.Add("Several tool calls failed before the final answer");
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

    private static bool IsDirectoryChange(FileChangeRecord change)
    {
        return string.Equals(change.Before, DesktopAgentService.DirectorySnapshotMarker, StringComparison.Ordinal) ||
               string.Equals(change.After, DesktopAgentService.DirectorySnapshotMarker, StringComparison.Ordinal) ||
               change.RelativePath.EndsWith("/", StringComparison.Ordinal) ||
               change.RelativePath.EndsWith("\\", StringComparison.Ordinal);
    }

    private static bool IsSearchTool(string toolName)
    {
        return toolName.Equals("hybrid_search", StringComparison.OrdinalIgnoreCase) ||
               toolName.Equals("symbol_search", StringComparison.OrdinalIgnoreCase) ||
               toolName.Equals("semantic_search", StringComparison.OrdinalIgnoreCase) ||
               toolName.Equals("grep_search", StringComparison.OrdinalIgnoreCase) ||
               toolName.Equals("glob_search", StringComparison.OrdinalIgnoreCase) ||
               toolName.Equals("list_directory", StringComparison.OrdinalIgnoreCase) ||
               toolName.Equals("plan_project_scaffold", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasGraphSignal(ToolReplayEntry entry)
    {
        return ContainsEvidenceSource(entry.ResultPreview, "graph");
    }

    private static bool HasMemorySignal(ToolReplayEntry entry)
    {
        return ContainsEvidenceSource(entry.ResultPreview, "memory");
    }

    private static bool HasGitSignal(ToolReplayEntry entry)
    {
        return ContainsEvidenceSource(entry.ResultPreview, "git");
    }

    private static bool ContainsEvidenceSource(string text, string source)
    {
        return text.Contains($"\"{source}\"", StringComparison.OrdinalIgnoreCase) ||
               text.Contains($": {source}", StringComparison.OrdinalIgnoreCase) ||
               text.Contains($"{source}:", StringComparison.OrdinalIgnoreCase);
    }
}
