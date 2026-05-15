namespace AgentQ.Desktop.Services;

public sealed class AgentCheckpointRunStep
{
    public string State { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
