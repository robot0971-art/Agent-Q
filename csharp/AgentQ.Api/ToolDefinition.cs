namespace AgentQ.Api;

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public object InputSchema { get; set; } = new();
}

public enum ToolChoiceType
{
    Auto,
    Any,
    Tool
}

public class ToolChoice
{
    public ToolChoiceType Type { get; set; }
    public string? Name { get; set; }

    public static ToolChoice Auto => new() { Type = ToolChoiceType.Auto };
    public static ToolChoice Any => new() { Type = ToolChoiceType.Any };
    public static ToolChoice Named(string name) => new() { Type = ToolChoiceType.Tool, Name = name };
}

