namespace AgentQ.Desktop.Services;

public sealed class GitPullSafetyAnalysis
{
    public bool CanPull { get; init; }

    public string Reason { get; init; } = string.Empty;
}
