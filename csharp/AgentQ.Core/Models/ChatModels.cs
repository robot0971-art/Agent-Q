using System.Text.Json.Serialization;

namespace AgentQ.Core.Models;

/// <summary>
/// Role of a chat message.
/// </summary>
public enum ChatRole
{
    /// <summary>System instruction.</summary>
    System,

    /// <summary>User message.</summary>
    User,

    /// <summary>Assistant message.</summary>
    Assistant,

    /// <summary>Tool message.</summary>
    Tool
}

/// <summary>
/// A chat message with one or more content blocks.
/// </summary>
public class ChatMessage
{
    /// <summary>Message role.</summary>
    public ChatRole Role { get; set; }

    /// <summary>Message content blocks.</summary>
    public List<ChatContent> Content { get; set; } = new();

    /// <summary>Whether this message was compacted from older history.</summary>
    public bool IsCompacted { get; set; }

    /// <summary>Optional compaction summary.</summary>
    public string? CompactionSummary { get; set; }

    /// <summary>Create a system text message.</summary>
    public static ChatMessage SystemText(string text) =>
        new() { Role = ChatRole.System, Content = new() { ChatContent.CreateText(text) } };

    /// <summary>Create a user text message.</summary>
    public static ChatMessage UserText(string text) =>
        new() { Role = ChatRole.User, Content = new() { ChatContent.CreateText(text) } };

    /// <summary>Create an assistant text message.</summary>
    public static ChatMessage AssistantText(string text) =>
        new() { Role = ChatRole.Assistant, Content = new() { ChatContent.CreateText(text) } };

    /// <summary>Create an assistant tool-use message.</summary>
    public static ChatMessage AssistantToolUse(string toolId, string toolName, object input) =>
        new() { Role = ChatRole.Assistant, Content = new() { ChatContent.CreateToolUse(toolId, toolName, input) } };

    /// <summary>Create a user tool-result message.</summary>
    public static ChatMessage UserToolResult(string toolUseId, string result, bool isError) =>
        new() { Role = ChatRole.User, Content = new() { ChatContent.CreateToolResult(toolUseId, result, isError) } };
}

/// <summary>
/// Type of a content block.
/// </summary>
public enum ContentType
{
    /// <summary>Text block.</summary>
    Text,

    /// <summary>Image block.</summary>
    Image,

    /// <summary>Tool-use block.</summary>
    ToolUse,

    /// <summary>Tool-result block.</summary>
    ToolResult
}

/// <summary>
/// A chat content block.
/// </summary>
public class ChatContent
{
    /// <summary>Content block type.</summary>
    public ContentType Type { get; set; }

    /// <summary>Text content for text blocks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>Media MIME type for image blocks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }

    /// <summary>Base64-encoded media data for image blocks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Base64Data { get; set; }

    /// <summary>Tool id for tool-use blocks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolId { get; set; }

    /// <summary>Tool name for tool-use blocks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; set; }

    /// <summary>Tool input for tool-use blocks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ToolInput { get; set; }

    /// <summary>Reasoning text associated with a content block.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningContent { get; set; }

    /// <summary>Tool-use id for tool-result blocks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; set; }

    /// <summary>Tool result content.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolResult { get; set; }

    /// <summary>Whether the tool result represents an error.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsToolError { get; set; }

    /// <summary>Create a text content block.</summary>
    public static ChatContent CreateText(string text) =>
        new() { Type = ContentType.Text, Text = text };

    /// <summary>Create an image content block.</summary>
    public static ChatContent CreateImage(string mediaType, string base64Data) =>
        new() { Type = ContentType.Image, MediaType = mediaType, Base64Data = base64Data };

    /// <summary>Create a tool-use content block.</summary>
    public static ChatContent CreateToolUse(string toolId, string toolName, object input) =>
        new()
        {
            Type = ContentType.ToolUse,
            ToolId = NormalizeToolIdentifier(toolId),
            ToolName = NormalizeToolIdentifier(toolName),
            ToolInput = input
        };

    /// <summary>Create a tool-result content block.</summary>
    public static ChatContent CreateToolResult(string toolUseId, string result, bool isError) =>
        new()
        {
            Type = ContentType.ToolResult,
            ToolUseId = NormalizeToolIdentifier(toolUseId),
            ToolResult = result,
            IsToolError = isError
        };

    private static string NormalizeToolIdentifier(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}

/// <summary>
/// Input context for a chat request.
/// </summary>
public class ChatContext
{
    /// <summary>Model name.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Messages to send.</summary>
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>Optional system prompt.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Maximum output tokens.</summary>
    public uint MaxTokens { get; set; } = 1024;

    /// <summary>Whether streaming is requested.</summary>
    public bool Stream { get; set; } = true;

    /// <summary>Maximum tool-loop steps for a single turn.</summary>
    public int MaxSteps { get; set; } = 45;
}

/// <summary>
/// Model response.
/// </summary>
public class ChatResponse
{
    /// <summary>Response id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Model name.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Response content blocks.</summary>
    public List<ChatContent> Content { get; set; } = new();

    /// <summary>Usage statistics.</summary>
    public UsageStats? Usage { get; set; }
}

/// <summary>
/// Token usage statistics.
/// </summary>
public class UsageStats
{
    /// <summary>Input token count.</summary>
    public int InputTokens { get; set; }

    /// <summary>Output token count.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Total token count.</summary>
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// A streamed response chunk.
/// </summary>
public class StreamChunk
{
    /// <summary>Text delta.</summary>
    public string? TextDelta { get; set; }

    /// <summary>Reasoning delta.</summary>
    public string? ReasoningDelta { get; set; }

    /// <summary>Tool-use delta.</summary>
    public ToolUseChunk? ToolUseDelta { get; set; }

    /// <summary>Whether the stream is complete.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Usage statistics.</summary>
    public UsageStats? Usage { get; set; }
}

/// <summary>
/// Tool-use data in a stream.
/// </summary>
public class ToolUseChunk
{
    /// <summary>Tool id.</summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>Tool name.</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Partial or complete tool input.</summary>
    public string? PartialInput { get; set; }

    /// <summary>Whether the tool-use chunk is complete.</summary>
    public bool IsComplete { get; set; }
}

/// <summary>
/// Tool definition exposed to a provider.
/// </summary>
public class ToolDefinition
{
    /// <summary>Tool name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tool description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Tool input schema.</summary>
    public object InputSchema { get; set; } = new();
}
