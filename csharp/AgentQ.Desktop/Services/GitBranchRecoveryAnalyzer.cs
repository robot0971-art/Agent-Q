namespace AgentQ.Desktop.Services;

public static class GitBranchRecoveryAnalyzer
{
    public static string CreateBackupBranchName(DateTime timestamp)
    {
        return $"backup/{timestamp:yyyyMMdd-HHmmss}";
    }

    public static string BuildRecoveryAdvice(string statusOutput, IReadOnlyCollection<GitChangedFile> changedFiles)
    {
        if (changedFiles.Count > 0)
        {
            return $"Recovery: {changedFiles.Count} local change(s). Commit or stash them before checkout or pull.";
        }

        var branchLine = statusOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("## ", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(branchLine))
        {
            return "Recovery: Git branch status is unavailable. Refresh status before changing branches.";
        }

        var summary = branchLine[3..].Trim();
        if (summary.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase))
        {
            return "Recovery: Detached HEAD. Create a backup branch before switching away.";
        }

        if (!summary.Contains("...", StringComparison.Ordinal))
        {
            return "Recovery: No upstream branch. Create a backup branch before switching to main.";
        }

        if (summary.Contains("[gone]", StringComparison.OrdinalIgnoreCase))
        {
            return "Recovery: Upstream is gone. Create a backup branch, then switch to main.";
        }

        var ahead = TryReadCount(summary, "ahead");
        var behind = TryReadCount(summary, "behind");

        return (ahead, behind) switch
        {
            ( > 0, > 0) => "Recovery: Branch diverged. Create a backup branch before reviewing or switching.",
            ( > 0, _) => "Recovery: Local commits exist. Create a backup branch or push before switching.",
            (_, > 0) => "Recovery: Clean and behind upstream. Pull --ff-only is the next safe step.",
            _ => "Recovery: Clean and up to date. Backup branch is optional."
        };
    }

    public static bool CanSwitchBranch(IReadOnlyCollection<GitChangedFile> changedFiles, out string reason)
    {
        if (changedFiles.Count > 0)
        {
            reason = "Commit, stash, or discard local changes before switching branches.";
            return false;
        }

        reason = "Working tree is clean.";
        return true;
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
