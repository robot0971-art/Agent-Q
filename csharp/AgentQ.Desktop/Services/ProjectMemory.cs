namespace AgentQ.Desktop.Services;

public sealed class ProjectMemory
{
    public string WorkspaceRoot { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<string> VerificationCommands { get; set; } = [];

    public List<string> ProjectHints { get; set; } = [];

    public List<string> WorkspaceRules { get; set; } = [];

    public List<ProjectMemoryLesson> Lessons { get; set; } = [];

    public List<ProjectMemoryPreference> Preferences { get; set; } = [];

    public List<ProjectMemoryCheck> Checks { get; set; } = [];

    public ProjectContextBank ContextBank { get; set; } = new();
}

public sealed class ProjectContextBank
{
    public List<ProjectMemoryFact> Stack { get; set; } = [];

    public List<ProjectMemoryFact> Rules { get; set; } = [];

    public List<ProjectMemoryFact> Preferences { get; set; } = [];

    public List<ProjectMemoryFact> ForbiddenPatterns { get; set; } = [];

    public List<ProjectMemoryFact> KeyCommands { get; set; } = [];

    public List<ProjectMemoryFact> KeyFiles { get; set; } = [];

    public List<ProjectMemoryFact> KeySymbols { get; set; } = [];

    public List<ProjectMemoryFact> RecurringErrors { get; set; } = [];
}

public sealed class ProjectMemoryFact
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public double Confidence { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public string Source { get; set; } = string.Empty;
}

public sealed class ProjectMemoryLesson
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public double Confidence { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? LastUsedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public bool Enabled { get; set; } = true;

    public string Source { get; set; } = string.Empty;
}

public sealed class ProjectMemoryPreference
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}

public sealed class ProjectMemoryCheck
{
    public string Name { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string When { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}
