namespace AgentQ.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public ITool? Get(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }

    public IReadOnlyCollection<ITool> All => _tools.Values;

    public List<ToolDefinitionEntry> GetToolDefinitions()
    {
        return _tools.Values.Select(t => new ToolDefinitionEntry
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        }).ToList();
    }
}

public class ToolDefinitionEntry
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public object InputSchema { get; init; } = new();
}

