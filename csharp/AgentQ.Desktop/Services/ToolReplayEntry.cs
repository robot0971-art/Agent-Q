namespace AgentQ.Desktop.Services;

public sealed class ToolReplayEntry
{
    public DateTime StartedAt { get; set; } = DateTime.Now;

    public DateTime CompletedAt { get; set; } = DateTime.Now;

    public string ToolName { get; set; } = string.Empty;

    public string ToolUseId { get; set; } = string.Empty;

    public string InputJson { get; set; } = string.Empty;

    public string ResultPreview { get; set; } = string.Empty;

    public bool IsError { get; set; }

    public int DurationMs { get; set; }
}
