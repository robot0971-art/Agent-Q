namespace AgentQ.Desktop.Services;

public sealed class ToolReplaySession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string WorkspaceRoot { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string PromptPreview { get; set; } = string.Empty;

    public List<ToolReplayEntry> Entries { get; set; } = [];
}
