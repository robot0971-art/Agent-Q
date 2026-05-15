namespace AgentQ.Desktop.Services;

public sealed class DesktopGitSnapshot
{
    public required GitCommandResult Status { get; init; }

    public required GitCommandResult DiffStat { get; init; }

    public required GitCommandResult FullDiff { get; init; }

    public IReadOnlyList<GitChangedFile> ChangedFiles { get; init; } = [];

    public bool Succeeded => Status.Succeeded;

    public bool HasChanges => Succeeded &&
                              !string.Equals(Status.DisplayOutput, "No changes.", StringComparison.Ordinal);
}
