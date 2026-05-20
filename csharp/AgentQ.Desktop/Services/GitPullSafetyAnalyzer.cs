namespace AgentQ.Desktop.Services;

public static class GitPullSafetyAnalyzer
{
    public static GitPullSafetyAnalysis Analyze(string statusOutput, IReadOnlyCollection<GitChangedFile> changedFiles)
    {
        if (changedFiles.Count > 0)
        {
            return Block("Commit, stash, or discard local changes before pulling.");
        }

        var branchLine = statusOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("## ", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(branchLine))
        {
            return Block("Git branch status is unavailable.");
        }

        var summary = branchLine[3..].Trim();
        if (summary.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase))
        {
            return Block("Detached HEAD cannot be pulled safely. Switch to a branch first.");
        }

        if (!summary.Contains("...", StringComparison.Ordinal))
        {
            return Block("No upstream branch is configured.");
        }

        if (summary.Contains("[gone]", StringComparison.OrdinalIgnoreCase))
        {
            return Block("The upstream branch is gone. Switch to the consolidated branch or create a backup before recovery.");
        }

        var ahead = TryReadCount(summary, "ahead");
        var behind = TryReadCount(summary, "behind");
        if (ahead > 0 && behind > 0)
        {
            return Block("The branch has diverged. Review local and remote commits before pulling.");
        }

        if (ahead > 0)
        {
            return Block("The branch has local commits that are not on the upstream branch.");
        }

        return new GitPullSafetyAnalysis
        {
            CanPull = true,
            Reason = behind > 0
                ? $"Safe to pull with fast-forward only. Behind by {behind} commit(s)."
                : "Safe to check for upstream updates with fast-forward only."
        };
    }

    private static GitPullSafetyAnalysis Block(string reason)
    {
        return new GitPullSafetyAnalysis
        {
            CanPull = false,
            Reason = reason
        };
    }

    private static int TryReadCount(string value, string label)
    {
        var marker = $"{label} ";
        var markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return 0;
        }

        var start = markerIndex + marker.Length;
        var end = start;
        while (end < value.Length && char.IsDigit(value[end]))
        {
            end++;
        }

        return int.TryParse(value[start..end], out var count) ? count : 0;
    }
}
