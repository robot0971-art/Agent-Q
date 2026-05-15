namespace AgentQ.Desktop.Services;

public sealed class DesktopGitPromptResult
{
    public required DesktopGitSnapshot Snapshot { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public string SuccessLog { get; init; } = string.Empty;

    public string NoChangesStatus { get; init; } = "No changes.";

    public string NoChangesLog { get; init; } = string.Empty;

    public string FailureStatus { get; init; } = "Git status failed";

    public string FailureLogPrefix { get; init; } = "Git unavailable";
}
