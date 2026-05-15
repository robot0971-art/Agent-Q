namespace AgentQ.Desktop.Services;

public sealed class ProjectMemory
{
    public string WorkspaceRoot { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<string> VerificationCommands { get; set; } = [];

    public List<string> ProjectHints { get; set; } = [];

    public List<string> WorkspaceRules { get; set; } = [];
}
