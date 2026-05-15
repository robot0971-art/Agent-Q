namespace AgentQ.Desktop.Services;

public sealed class AgentCheckpointMessage
{
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
