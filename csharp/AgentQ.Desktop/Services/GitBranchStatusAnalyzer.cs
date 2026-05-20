namespace AgentQ.Desktop.Services;

public static class GitBranchStatusAnalyzer
{
    public static string Analyze(string statusOutput)
    {
        var branchLine = statusOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("## ", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(branchLine))
        {
            return string.Empty;
        }

        var summary = branchLine[3..].Trim();
        if (summary.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase))
        {
            return "Branch: detached HEAD. Create or switch to a branch before committing.";
        }

        if (!summary.Contains("...", StringComparison.Ordinal))
        {
            var branchName = summary.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return $"Branch: {branchName}. No upstream is configured; pull/push behavior may need an explicit remote branch.";
        }

        var branchPart = summary.Split(' ', 2, StringSplitOptions.TrimEntries)[0];
        var branchParts = branchPart.Split("...", 2, StringSplitOptions.None);
        var localBranch = branchParts[0];
        var upstreamBranch = branchParts.Length > 1 ? branchParts[1] : "upstream";

        if (summary.Contains("[gone]", StringComparison.OrdinalIgnoreCase))
        {
            return $"Branch: {localBranch}. Upstream {upstreamBranch} is gone; switch to the consolidated branch or create a backup before resetting.";
        }

        var ahead = TryReadCount(summary, "ahead");
        var behind = TryReadCount(summary, "behind");

        return (ahead, behind) switch
        {
            (> 0, > 0) => $"Branch: {localBranch}. Diverged from {upstreamBranch}: ahead {ahead}, behind {behind}. Review before pulling or resetting.",
            (> 0, _) => $"Branch: {localBranch}. Ahead of {upstreamBranch} by {ahead} commit(s). Push or preserve these commits before rebasing/resetting.",
            (_, > 0) => $"Branch: {localBranch}. Behind {upstreamBranch} by {behind} commit(s). Pull is likely needed.",
            _ => $"Branch: {localBranch}. Tracking {upstreamBranch}."
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
