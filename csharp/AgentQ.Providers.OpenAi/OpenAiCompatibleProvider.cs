using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;

namespace AgentQ.Providers.OpenAi;

public class OpenAiCompatibleProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public string Name => "openai";
    public string DefaultModel => _model;

    public OpenAiCompatibleProvider(string baseUrl, string apiKey, string model = "gpt-4o")
    {
        _model = model;
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task<ChatResponse> GenerateResponseAsync(
        ChatContext context,
        IEnumerable<AgentQ.Core.Models.ToolDefinition> tools,
        CancellationToken ct = default)
    {
        var request = CreateChatRequest(context, tools, false);
        var json = JsonSerializer.Serialize(request, GetJsonOptions());

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("/v1/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var chatResponse = JsonSerializer.Deserialize<OpenAiChatResponse>(body, GetJsonOptions());

        return ConvertToChatResponse(chatResponse!);
    }

    public async IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
        ChatContext context,
        IEnumerable<AgentQ.Core.Models.ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = CreateChatRequest(context, tools, true);
        var json = JsonSerializer.Serialize(request, GetJsonOptions());

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var requestMsg = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions") { Content = content };
        using var response = await _httpClient.SendAsync(requestMsg, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var toolCalls = new Dictionary<int, ToolCallAccumulator>();

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line.Substring(6).Trim();
            if (data == "[DONE]")
            {
                yield break;
            }

            OpenAiStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(data, GetJsonOptions());
            }
            catch
            {
                continue;
            }

            if (chunk?.Choices == null || chunk.Choices.Count == 0)
            {
                continue;
            }

            foreach (var choice in chunk.Choices)
            {
                var delta = choice.Delta;

                if (!string.IsNullOrEmpty(delta?.Content))
                {
                    yield return new StreamChunk { TextDelta = delta.Content };
                }

                if (delta?.ToolCalls != null)
                {
                    foreach (var toolCall in delta.ToolCalls)
                    {
                        var accumulator = GetAccumulator(toolCalls, toolCall.Index);

                        if (!string.IsNullOrEmpty(toolCall.Id))
                        {
                            accumulator.ToolId = toolCall.Id;
                        }

                        if (!string.IsNullOrEmpty(toolCall.Function?.Name))
                        {
                            accumulator.ToolName = toolCall.Function.Name;
                        }

                        if (!string.IsNullOrEmpty(toolCall.Function?.Arguments))
                        {
                            accumulator.Arguments.Append(toolCall.Function.Arguments);
                        }
                    }
                }

                if (choice.FinishReason == "tool_calls")
                {
                    foreach (var toolCall in toolCalls.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value))
                    {
                        if (string.IsNullOrEmpty(toolCall.ToolId))
                        {
                            continue;
                        }

                        yield return new StreamChunk
                        {
                            ToolUseDelta = new ToolUseChunk
                            {
                                ToolId = toolCall.ToolId,
                                ToolName = toolCall.ToolName ?? "unknown",
                                PartialInput = toolCall.Arguments.ToString(),
                                IsComplete = true
                            }
                        };
                    }

                    toolCalls.Clear();
                }

                if (choice.FinishReason != null)
                {
                    yield return new StreamChunk { IsComplete = true };
                }
            }
        }
    }

    private static ToolCallAccumulator GetAccumulator(Dictionary<int, ToolCallAccumulator> toolCalls, int index)
    {
        if (!toolCalls.TryGetValue(index, out var accumulator))
        {
            accumulator = new ToolCallAccumulator();
            toolCalls[index] = accumulator;
        }

        return accumulator;
    }

    private OpenAiChatRequest CreateChatRequest(ChatContext context, IEnumerable<AgentQ.Core.Models.ToolDefinition> tools, bool stream)
    {
        var messages = new List<OpenAiMessage>();

        if (!string.IsNullOrEmpty(context.SystemPrompt))
        {
            messages.Add(new OpenAiMessage { Role = "system", Content = context.SystemPrompt });
        }

        foreach (var msg in context.Messages)
        {
            var role = msg.Role switch
            {
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.System => "system",
                ChatRole.Tool => "tool",
                _ => "user"
            };

            var openAiMsg = new OpenAiMessage { Role = role };

            if (msg.Content.Count == 1 && msg.Content[0].Type == ContentType.Text)
            {
                openAiMsg.Content = msg.Content[0].Text;
            }
            else if (msg.Content.Any(c => c.Type == ContentType.ToolUse))
            {
                var toolUses = msg.Content.Where(c => c.Type == ContentType.ToolUse).ToList();
                if (toolUses.Any())
                {
                    openAiMsg.ToolCalls = toolUses.Select(t => new OpenAiToolCall
                    {
                        Id = t.ToolId,
                        Type = "function",
                        Function = new OpenAiFunctionCall
                        {
                            Name = t.ToolName,
                            Arguments = SerializeToolArguments(t.ToolInput)
                        }
                    }).ToList();
                }

                var textContent = msg.Content.Where(c => c.Type == ContentType.Text).Select(c => c.Text).FirstOrDefault();
                if (!string.IsNullOrEmpty(textContent))
                {
                    openAiMsg.Content = textContent;
                }
            }
            else if (msg.Content.Any(c => c.Type == ContentType.ToolResult))
            {
                var toolResult = msg.Content.First(c => c.Type == ContentType.ToolResult);
                openAiMsg.Role = "tool";
                openAiMsg.ToolCallId = toolResult.ToolUseId;
                openAiMsg.Content = toolResult.ToolResult;
            }
            else
            {
                openAiMsg.Content = string.Join("\n", msg.Content.Where(c => c.Type == ContentType.Text).Select(c => c.Text));
            }

            messages.Add(openAiMsg);
        }

        var request = new OpenAiChatRequest
        {
            Model = string.IsNullOrEmpty(context.Model) ? DefaultModel : context.Model,
            Messages = messages,
            MaxTokens = context.MaxTokens == 0 ? 1024 : (int)context.MaxTokens,
            Stream = stream
        };

        var toolList = tools.ToList();
        if (toolList.Any())
        {
            request.Tools = toolList.Select(t => new OpenAiToolDefinition
            {
                Type = "function",
                Function = new OpenAiFunctionDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.InputSchema
                }
            }).ToList();
        }

        return request;
    }

    private ChatResponse ConvertToChatResponse(OpenAiChatResponse response)
    {
        var chatResponse = new ChatResponse
        {
            Id = response.Id ?? Guid.NewGuid().ToString(),
            Model = response.Model ?? DefaultModel,
            Content = new List<ChatContent>()
        };

        if (response.Usage != null)
        {
            chatResponse.Usage = new UsageStats
            {
                InputTokens = response.Usage.PromptTokens,
                OutputTokens = response.Usage.CompletionTokens
            };
        }

        if (response.Choices != null && response.Choices.Count > 0)
        {
            var choice = response.Choices[0];

            if (!string.IsNullOrEmpty(choice.Message?.Content))
            {
                chatResponse.Content.Add(ChatContent.CreateText(choice.Message.Content));
            }

            if (choice.Message?.ToolCalls != null)
            {
                foreach (var toolCall in choice.Message.ToolCalls)
                {
                    chatResponse.Content.Add(ChatContent.CreateToolUse(
                        toolCall.Id ?? Guid.NewGuid().ToString(),
                        toolCall.Function?.Name ?? "unknown",
                        toolCall.Function?.Arguments ?? "{}"));
                }
            }
        }

        return chatResponse;
    }

    private static string SerializeToolArguments(object? toolInput)
    {
        return toolInput switch
        {
            null => "{}",
            string rawJson => rawJson,
            JsonElement element => element.GetRawText(),
            _ => JsonSerializer.Serialize(toolInput)
        };
    }

    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}

sealed class ToolCallAccumulator
{
    public string? ToolId { get; set; }
    public string? ToolName { get; set; }
    public StringBuilder Arguments { get; } = new();
}

public class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAiMessage> Messages { get; set; } = new();

    [JsonPropertyName("tools")]
    public List<OpenAiToolDefinition>? Tools { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

public class OpenAiMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; set; }
}

public class OpenAiToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAiFunctionDefinition Function { get; set; } = new();
}

public class OpenAiFunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public object Parameters { get; set; } = new();
}

public class OpenAiChatResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenAiChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }
}

public class OpenAiChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

public class OpenAiStreamChunk
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenAiStreamChoice>? Choices { get; set; }
}

public class OpenAiStreamChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public OpenAiStreamDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public class OpenAiStreamDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; set; }
}

public class OpenAiToolCall
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public OpenAiFunctionCall? Function { get; set; }
}

public class OpenAiFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

