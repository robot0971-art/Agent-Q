namespace AgentQ.Desktop.Services;

public sealed class ProjectAgentConfig
{
    public string WorkMode { get; set; } = AgentWorkMode.Coding.ToString();

    public List<string> VerificationCommands { get; set; } = [];

    public List<string> WorkspaceRules { get; set; } = [];

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
