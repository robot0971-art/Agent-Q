namespace AgentQ.Tools;

/// <summary>
/// Common interface for tools that can be exposed to an agent loop.
/// </summary>
public interface ITool
{
    /// <summary>
    /// Tool name used in provider tool calls.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable tool description.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Tool input JSON schema.
    /// </summary>
    object InputSchema { get; }

    /// <summary>
    /// True when the tool requires user permission before execution.
    /// </summary>
    bool RequiresPermission { get; }

    /// <summary>
    /// Executes the tool.
    /// </summary>
    Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default);
}

/// <summary>
/// Result returned by a tool execution.
/// </summary>
public class ToolResult
{
    /// <summary>
    /// Tool result content.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// True when the tool failed.
    /// </summary>
    public bool IsError { get; init; }

    /// <summary>
    /// Structured error message when the tool failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful tool result.
    /// </summary>
    public static ToolResult Success(string content) => new() { Content = content };

    /// <summary>
    /// Creates a failed tool result.
    /// </summary>
    public static ToolResult Error(string message) => new() { Content = message, IsError = true, ErrorMessage = message };
}
