namespace AgentQ.Tools;

/// <summary>
/// Registry of tools available to the agent loop.
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly List<string> _duplicateRegistrations = [];

    /// <summary>
    /// Registers a tool, optionally replacing an existing tool with the same name.
    /// </summary>
    public void Register(ITool tool, bool replace = false)
    {
        if (_tools.ContainsKey(tool.Name) && !replace)
        {
            throw new InvalidOperationException($"Tool already registered: {tool.Name}");
        }

        _tools[tool.Name] = tool;
    }

    public bool TryRegister(ITool tool)
    {
        if (_tools.ContainsKey(tool.Name))
        {
            _duplicateRegistrations.Add(tool.Name);
            return false;
        }

        _tools[tool.Name] = tool;
        return true;
    }

    /// <summary>
    /// Looks up a registered tool by name.
    /// </summary>
    public ITool? Get(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }

    /// <summary>
    /// All registered tools.
    /// </summary>
    public IReadOnlyCollection<ITool> All => _tools.Values;

    public IReadOnlyList<string> DuplicateRegistrations => _duplicateRegistrations;

    /// <summary>
    /// Builds provider-facing tool definitions.
    /// </summary>
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

/// <summary>
/// Provider-facing tool definition entry.
/// </summary>
public class ToolDefinitionEntry
{
    /// <summary>
    /// Tool name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Tool description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Tool input JSON schema.
    /// </summary>
    public object InputSchema { get; init; } = new();
}
