namespace AgentQ.Desktop.Services;

public static class GitBranchRecoveryAnalyzer
{
    public static string CreateBackupBranchName(DateTime timestamp)
    {
        return $"backup/desktop-recovery-{timestamp:yyyyMMdd-HHmmss}";
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
}
