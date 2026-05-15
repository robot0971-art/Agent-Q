namespace AgentQ.Desktop.Services;

public sealed class AgentCheckpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string WorkspaceRoot { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string StatusText { get; set; } = string.Empty;

    public string PendingInput { get; set; } = string.Empty;

    public string GitStatus { get; set; } = string.Empty;

    public string GitDiffStat { get; set; } = string.Empty;

    public List<AgentCheckpointMessage> Conversation { get; set; } = [];

    public List<string> Logs { get; set; } = [];

    public List<AgentCheckpointRunStep> RunSteps { get; set; } = [];

    public List<AgentCheckpointPlanItem> PlanItems { get; set; } = [];

    public string Summary => $"{CreatedAt:yyyy-MM-dd HH:mm:ss} - {StatusText}";
}
