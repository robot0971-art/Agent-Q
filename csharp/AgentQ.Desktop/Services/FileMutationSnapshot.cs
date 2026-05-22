namespace AgentQ.Desktop.Services;

public sealed class FileMutationSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string WorkspaceRoot { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public bool ExistedBefore { get; set; }

    public bool ExistsAfter { get; set; }

    public string Before { get; set; } = string.Empty;

    public string After { get; set; } = string.Empty;
}
