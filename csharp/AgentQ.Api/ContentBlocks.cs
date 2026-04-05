using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentQ.Api;

[JsonConverter(typeof(ContentBlockTypeConverter))]
public enum ContentBlockType
{
    Text,
    ToolUse,
    ToolResult
}

public class ContentBlockTypeConverter : JsonConverter<ContentBlockType>
{
    public override ContentBlockType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() switch
        {
            "text" => ContentBlockType.Text,
            "tool_use" => ContentBlockType.ToolUse,
            "tool_result" => ContentBlockType.ToolResult,
            _ => ContentBlockType.Text
        };
    }

    public override void Write(Utf8JsonWriter writer, ContentBlockType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ContentBlockType.Text => "text",
            ContentBlockType.ToolUse => "tool_use",
            ContentBlockType.ToolResult => "tool_result",
            _ => "text"
        });
    }
}

public class InputContentBlock
{
    public ContentBlockType Type { get; set; }
    public int Index { get; set; }
    [JsonPropertyName("text")]
    public string? TextContent { get; set; }
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("input")]
    public object? Input { get; set; }
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }
    [JsonPropertyName("content")]
    public List<ToolResultContentItem>? Content { get; set; }
    [JsonPropertyName("is_error")]
    public bool IsError { get; set; }

    public static InputContentBlock CreateText(string text) =>
        new() { Type = ContentBlockType.Text, TextContent = text };

    public static InputContentBlock CreateToolUse(string id, string name, object input) =>
        new() { Type = ContentBlockType.ToolUse, Id = id, Name = name, Input = input };

    public static InputContentBlock CreateToolResult(string toolUseId, string content, bool isError) =>
        new()
        {
            Type = ContentBlockType.ToolResult,
            ToolUseId = toolUseId,
            Content = new List<ToolResultContentItem> { ToolResultContentItem.CreateText(content) },
            IsError = isError
        };
}

public class ToolResultContentItem
{
    public string ContentType { get; set; } = "text";
    public string? Text { get; set; }
    public object? JsonValue { get; set; }

    public static ToolResultContentItem CreateText(string text) =>
        new() { ContentType = "text", Text = text };

    public static ToolResultContentItem CreateJson(object value) =>
        new() { ContentType = "json", JsonValue = value };
}

