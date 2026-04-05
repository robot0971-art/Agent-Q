using System.Text.Json.Serialization;

namespace AgentQ.Core.Models;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public class ChatMessage
{
    public ChatRole Role { get; set; }
    public List<ChatContent> Content { get; set; } = new();

    public static ChatMessage SystemText(string text) =>
        new() { Role = ChatRole.System, Content = new() { ChatContent.CreateText(text) } };

    public static ChatMessage UserText(string text) =>
        new() { Role = ChatRole.User, Content = new() { ChatContent.CreateText(text) } };

    public static ChatMessage AssistantText(string text) =>
        new() { Role = ChatRole.Assistant, Content = new() { ChatContent.CreateText(text) } };

    public static ChatMessage AssistantToolUse(string toolId, string toolName, object input) =>
        new() { Role = ChatRole.Assistant, Content = new() { ChatContent.CreateToolUse(toolId, toolName, input) } };

    public static ChatMessage UserToolResult(string toolUseId, string result, bool isError) =>
        new() { Role = ChatRole.User, Content = new() { ChatContent.CreateToolResult(toolUseId, result, isError) } };
}

public enum ContentType
{
    Text,
    ToolUse,
    ToolResult
}

public class ChatContent
{
    public ContentType Type { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ToolInput { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolResult { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsToolError { get; set; }

    public static ChatContent CreateText(string text) =>
        new() { Type = ContentType.Text, Text = text };

    public static ChatContent CreateToolUse(string toolId, string toolName, object input) =>
        new() { Type = ContentType.ToolUse, ToolId = toolId, ToolName = toolName, ToolInput = input };

    public static ChatContent CreateToolResult(string toolUseId, string result, bool isError) =>
        new() { Type = ContentType.ToolResult, ToolUseId = toolUseId, ToolResult = result, IsToolError = isError };
}

public class ChatContext
{
    public string Model { get; set; } = string.Empty;
    public List<ChatMessage> Messages { get; set; } = new();
    public string? SystemPrompt { get; set; }
    public uint MaxTokens { get; set; } = 1024;
    public bool Stream { get; set; } = true;
}

public class ChatResponse
{
    public string Id { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public List<ChatContent> Content { get; set; } = new();
    public UsageStats? Usage { get; set; }
}

public class UsageStats
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens => InputTokens + OutputTokens;
}

public class StreamChunk
{
    public string? TextDelta { get; set; }
    public ToolUseChunk? ToolUseDelta { get; set; }
    public bool IsComplete { get; set; }
    public UsageStats? Usage { get; set; }
}

public class ToolUseChunk
{
    public string ToolId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string? PartialInput { get; set; }
    public bool IsComplete { get; set; }
}

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public object InputSchema { get; set; } = new();
}

