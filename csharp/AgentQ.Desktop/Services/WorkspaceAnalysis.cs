namespace AgentQ.Desktop.Services;

public sealed class WorkspaceAnalysis
{
    public string WorkspaceRoot { get; set; } = string.Empty;

    public string ProjectType { get; set; } = "Unknown";

    public string Framework { get; set; } = "Unknown";

    public string GitBranch { get; set; } = "Not a Git repository";

    public int FileCount { get; set; }

    public int DirectoryCount { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<string> VerificationCommands { get; set; } = [];

    public List<string> ProjectMap { get; set; } = [];

    public List<string> KeyFiles { get; set; } = [];

    public List<string> Hints { get; set; } = [];

    public string Summary => $"{ProjectType} / {Framework}";
}
