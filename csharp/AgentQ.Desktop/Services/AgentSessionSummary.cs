namespace AgentQ.Desktop.Services;

public sealed class AgentSessionSummary
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string WorkspaceRoot { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string Title { get; set; } = "AgentQ session";

    public string Narrative { get; set; } = string.Empty;

    public List<string> CompletedWork { get; set; } = [];

    public List<string> ChangedFiles { get; set; } = [];

    public List<string> VerificationResults { get; set; } = [];

    public List<string> OpenPlanItems { get; set; } = [];

    public List<string> NextSteps { get; set; } = [];

    public string DisplayText
    {
        get
        {
            var lines = new List<string>
            {
                $"Session: {CreatedAt:yyyy-MM-dd HH:mm:ss}",
                $"Workspace: {WorkspaceRoot}",
                $"Title: {Title}"
            };

            AddSection(lines, "Summary", [Narrative]);
            AddSection(lines, "Completed work", CompletedWork);
            AddSection(lines, "Changed files", ChangedFiles);
            AddSection(lines, "Verification", VerificationResults);
            AddSection(lines, "Open plan items", OpenPlanItems);
            AddSection(lines, "Next steps", NextSteps);

            return string.Join(Environment.NewLine, lines).TrimEnd();
        }
    }

    private static void AddSection(List<string> lines, string title, IEnumerable<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (items.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add($"{title}:");
        foreach (var item in items)
        {
            lines.Add($"- {item}");
        }
    }
}
