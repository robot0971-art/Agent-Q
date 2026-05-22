namespace AgentQ.Desktop.Services;

public sealed class McpServerConfig
{
    public string Name { get; set; } = string.Empty;

    public string Transport { get; set; } = "stdio";

    public string Command { get; set; } = string.Empty;

    public List<string> Args { get; set; } = [];

    public string WorkingDirectory { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public List<string> Tags { get; set; } = [];

    public string DisplayText
    {
        get
        {
            var command = string.IsNullOrWhiteSpace(Command) ? "(no command)" : Command;
            var args = Args.Count == 0 ? string.Empty : " " + string.Join(' ', Args);
            return $"{Name} [{Transport}] {(Enabled ? "enabled" : "disabled")} - {command}{args}";
        }
    }
}
