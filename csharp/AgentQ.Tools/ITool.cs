namespace AgentQ.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    object InputSchema { get; }
    bool RequiresPermission { get; }
    Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default);
}

public class ToolResult
{
    public string Content { get; init; } = string.Empty;
    public bool IsError { get; init; }
    public string? ErrorMessage { get; init; }

    public static ToolResult Success(string content) => new() { Content = content };
    public static ToolResult Error(string message) => new() { Content = message, IsError = true, ErrorMessage = message };
}

