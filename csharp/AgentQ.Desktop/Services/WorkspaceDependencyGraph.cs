namespace AgentQ.Desktop.Services;

public sealed class WorkspaceDependencyGraph
{
    public List<WorkspaceDependencyEdge> Edges { get; } = [];

    public int FilesIndexed { get; set; }

    public int EdgeCount => Edges.Count;
}
