using System.Text;
using System.Text.Json;

namespace AgentQ.MockService;

public static class SseBuilder
{
    private const string DefaultModel = "claude-sonnet-4-6";

    public static string StreamingText()
    {
        var sb = new StringBuilder();

        AppendSse(sb, "message_start", new
        {
            type = "message_start",
            message = new
            {
                id = "msg_streaming_text",
                type = "message",
                role = "assistant",
                content = Array.Empty<object>(),
                model = DefaultModel,
                stop_reason = (string?)null,
                stop_sequence = (string?)null,
                usage = UsageJson(11, 0)
            }
        });

        AppendSse(sb, "content_block_start", new
        {
            type = "content_block_start",
            index = 0,
            content_block = new { type = "text", text = "" }
        });

        AppendSse(sb, "content_block_delta", new
        {
            type = "content_block_delta",
            index = 0,
            delta = new { type = "text_delta", text = "Mock streaming " }
        });

        AppendSse(sb, "content_block_delta", new
        {
            type = "content_block_delta",
            index = 0,
            delta = new { type = "text_delta", text = "says hello from the parity harness." }
        });

        AppendSse(sb, "content_block_stop", new
        {
            type = "content_block_stop",
            index = 0
        });

        AppendSse(sb, "message_delta", new
        {
            type = "message_delta",
            delta = new { stop_reason = "end_turn", stop_sequence = (string?)null },
            usage = UsageJson(11, 8)
        });

        AppendSse(sb, "message_stop", new { type = "message_stop" });

        return sb.ToString();
    }

    public static string ToolUse(string toolId, string toolName, string[] partialJsonChunks)
    {
        return ToolUses(new[] { new ToolUseInfo(toolId, toolName, partialJsonChunks) });
    }

    public static string ToolUses(ToolUseInfo[] toolUses)
    {
        var sb = new StringBuilder();
        var messageId = toolUses.FirstOrDefault()?.ToolId ?? "msg_tool_use";

        AppendSse(sb, "message_start", new
        {
            type = "message_start",
            message = new
            {
                id = $"msg_{messageId}",
                type = "message",
                role = "assistant",
                content = Array.Empty<object>(),
                model = DefaultModel,
                stop_reason = (string?)null,
                stop_sequence = (string?)null,
                usage = UsageJson(12, 0)
            }
        });

        for (int i = 0; i < toolUses.Length; i++)
        {
            var toolUse = toolUses[i];
            AppendSse(sb, "content_block_start", new
            {
                type = "content_block_start",
                index = i,
                content_block = new
                {
                    type = "tool_use",
                    id = toolUse.ToolId,
                    name = toolUse.ToolName,
                    input = new { }
                }
            });

            foreach (var chunk in toolUse.PartialJsonChunks)
            {
                AppendSse(sb, "content_block_delta", new
                {
                    type = "content_block_delta",
                    index = i,
                    delta = new { type = "input_json_delta", partial_json = chunk }
                });
            }

            AppendSse(sb, "content_block_stop", new
            {
                type = "content_block_stop",
                index = i
            });
        }

        AppendSse(sb, "message_delta", new
        {
            type = "message_delta",
            delta = new { stop_reason = "tool_use", stop_sequence = (string?)null },
            usage = UsageJson(12, 4)
        });

        AppendSse(sb, "message_stop", new { type = "message_stop" });

        return sb.ToString();
    }

    public static string FinalText(string text)
    {
        var sb = new StringBuilder();
        var messageId = $"msg_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        AppendSse(sb, "message_start", new
        {
            type = "message_start",
            message = new
            {
                id = messageId,
                type = "message",
                role = "assistant",
                content = Array.Empty<object>(),
                model = DefaultModel,
                stop_reason = (string?)null,
                stop_sequence = (string?)null,
                usage = UsageJson(14, 0)
            }
        });

        AppendSse(sb, "content_block_start", new
        {
            type = "content_block_start",
            index = 0,
            content_block = new { type = "text", text = "" }
        });

        AppendSse(sb, "content_block_delta", new
        {
            type = "content_block_delta",
            index = 0,
            delta = new { type = "text_delta", text = text }
        });

        AppendSse(sb, "content_block_stop", new
        {
            type = "content_block_stop",
            index = 0
        });

        AppendSse(sb, "message_delta", new
        {
            type = "message_delta",
            delta = new { stop_reason = "end_turn", stop_sequence = (string?)null },
            usage = UsageJson(14, 7)
        });

        AppendSse(sb, "message_stop", new { type = "message_stop" });

        return sb.ToString();
    }

    public static string FinalTextWithUsage(string text, uint inputTokens, uint outputTokens)
    {
        var sb = new StringBuilder();
        var messageId = $"msg_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        AppendSse(sb, "message_start", new
        {
            type = "message_start",
            message = new
            {
                id = messageId,
                type = "message",
                role = "assistant",
                content = Array.Empty<object>(),
                model = DefaultModel,
                stop_reason = (string?)null,
                stop_sequence = (string?)null,
                usage = new { input_tokens = inputTokens, cache_creation_input_tokens = 0, cache_read_input_tokens = 0, output_tokens = 0 }
            }
        });

        AppendSse(sb, "content_block_start", new
        {
            type = "content_block_start",
            index = 0,
            content_block = new { type = "text", text = "" }
        });

        AppendSse(sb, "content_block_delta", new
        {
            type = "content_block_delta",
            index = 0,
            delta = new { type = "text_delta", text = text }
        });

        AppendSse(sb, "content_block_stop", new
        {
            type = "content_block_stop",
            index = 0
        });

        AppendSse(sb, "message_delta", new
        {
            type = "message_delta",
            delta = new { stop_reason = "end_turn", stop_sequence = (string?)null },
            usage = new { input_tokens = inputTokens, cache_creation_input_tokens = 0, cache_read_input_tokens = 0, output_tokens = outputTokens }
        });

        AppendSse(sb, "message_stop", new { type = "message_stop" });

        return sb.ToString();
    }

    private static void AppendSse(StringBuilder sb, string eventName, object data)
    {
        sb.AppendLine($"event: {eventName}");
        sb.AppendLine($"data: {JsonSerializer.Serialize(data)}");
        sb.AppendLine();
    }

    private static object UsageJson(uint inputTokens, uint outputTokens)
    {
        return new
        {
            input_tokens = inputTokens,
            cache_creation_input_tokens = 0,
            cache_read_input_tokens = 0,
            output_tokens = outputTokens
        };
    }
}

public record ToolUseInfo(string ToolId, string ToolName, string[] PartialJsonChunks);

