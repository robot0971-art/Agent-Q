using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentQ.Api;

public class MessageResponse
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "message";
    public string Role { get; set; } = "assistant";
    public List<OutputContentBlock> Content { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public string? StopReason { get; set; }
    public string? StopSequence { get; set; }
    public Usage Usage { get; set; } = new();
    public string? RequestId { get; set; }

    public uint TotalTokens => Usage.TotalTokens;
}

[JsonConverter(typeof(OutputContentBlockTypeConverter))]
public enum OutputContentBlockType
{
    Text,
    ToolUse,
    Thinking,
    RedactedThinking
}

public class OutputContentBlockTypeConverter : JsonConverter<OutputContentBlockType>
{
    public override OutputContentBlockType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() switch
        {
            "text" => OutputContentBlockType.Text,
            "tool_use" => OutputContentBlockType.ToolUse,
            "thinking" => OutputContentBlockType.Thinking,
            "redacted_thinking" => OutputContentBlockType.RedactedThinking,
            _ => OutputContentBlockType.Text
        };
    }

    public override void Write(Utf8JsonWriter writer, OutputContentBlockType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            OutputContentBlockType.Text => "text",
            OutputContentBlockType.ToolUse => "tool_use",
            OutputContentBlockType.Thinking => "thinking",
            OutputContentBlockType.RedactedThinking => "redacted_thinking",
            _ => "text"
        });
    }
}

public class OutputContentBlock
{
    public OutputContentBlockType Type { get; set; }
    public string? Text { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public object? Input { get; set; }
    public string? Thinking { get; set; }
    public string? Signature { get; set; }
    public object? Data { get; set; }
}

public class Usage
{
    public uint InputTokens { get; set; }
    public uint CacheCreationInputTokens { get; set; }
    public uint CacheReadInputTokens { get; set; }
    public uint OutputTokens { get; set; }

    public uint TotalTokens => InputTokens + OutputTokens + CacheCreationInputTokens + CacheReadInputTokens;
}

