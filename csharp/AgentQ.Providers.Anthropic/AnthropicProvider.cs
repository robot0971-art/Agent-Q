using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using AgentQ.Api;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;

namespace AgentQ.Providers.Anthropic;

public class AnthropicProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public string Name => "anthropic";
    public string DefaultModel => "claude-sonnet-4-6";

    public AnthropicProvider(string baseUrl, string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<ChatResponse> GenerateResponseAsync(
        ChatContext context,
        IEnumerable<AgentQ.Core.Models.ToolDefinition> tools,
        CancellationToken ct = default)
    {
        var request = CreateMessageRequest(context, tools, false);
        var json = JsonSerializer.Serialize(request, GetJsonOptions());

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("/v1/messages", content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var messageResponse = JsonSerializer.Deserialize<MessageResponse>(body, GetJsonOptions());

        return ConvertToChatResponse(messageResponse!);
    }

    public async IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
        ChatContext context,
        IEnumerable<AgentQ.Core.Models.ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = CreateMessageRequest(context, tools, true);
        var json = JsonSerializer.Serialize(request, GetJsonOptions());

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var requestMsg = new HttpRequestMessage(HttpMethod.Post, "/v1/messages") { Content = content };
        using var response = await _httpClient.SendAsync(requestMsg, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var currentToolUse = new Dictionary<int, (string id, string name, StringBuilder input)>();
        StreamChunk? currentChunk = null;

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (line.StartsWith("event:"))
            {
                var eventType = line.Substring("event:".Length).Trim();
                var dataLine = await reader.ReadLineAsync(ct);

                if (dataLine?.StartsWith("data:") == true)
                {
                    var data = dataLine.Substring("data:".Length).Trim();
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    switch (eventType)
                    {
                        case "content_block_start":
                            var index = root.GetProperty("index").GetInt32();
                            var contentBlock = root.GetProperty("content_block");
                            var type = contentBlock.GetProperty("type").GetString();

                            if (type == "text")
                            {
                                currentChunk = new StreamChunk { TextDelta = "" };
                                yield return currentChunk;
                            }
                            else if (type == "tool_use")
                            {
                                var id = contentBlock.GetProperty("id").GetString()!;
                                var name = contentBlock.GetProperty("name").GetString()!;
                                currentToolUse[index] = (id, name, new StringBuilder());
                                currentChunk = new StreamChunk
                                {
                                    ToolUseDelta = new ToolUseChunk
                                    {
                                        ToolId = id,
                                        ToolName = name,
                                        PartialInput = ""
                                    }
                                };
                                yield return currentChunk;
                            }
                            break;

                        case "content_block_delta":
                            var deltaIndex = root.GetProperty("index").GetInt32();
                            var delta = root.GetProperty("delta");
                            var deltaType = delta.GetProperty("type").GetString();

                            if (deltaType == "text_delta")
                            {
                                var text = delta.GetProperty("text").GetString() ?? "";
                                yield return new StreamChunk { TextDelta = text };
                            }
                            else if (deltaType == "input_json_delta")
                            {
                                var partialJson = delta.GetProperty("partial_json").GetString() ?? "";
                                if (currentToolUse.TryGetValue(deltaIndex, out var tool))
                                {
                                    tool.input.Append(partialJson);
                                    yield return new StreamChunk
                                    {
                                        ToolUseDelta = new ToolUseChunk
                                        {
                                            ToolId = tool.id,
                                            ToolName = tool.name,
                                            PartialInput = partialJson
                                        }
                                    };
                                }
                            }
                            break;

                        case "content_block_stop":
                            var stopIndex = root.GetProperty("index").GetInt32();
                            if (currentToolUse.TryGetValue(stopIndex, out var completedTool))
                            {
                                yield return new StreamChunk
                                {
                                    ToolUseDelta = new ToolUseChunk
                                    {
                                        ToolId = completedTool.id,
                                        ToolName = completedTool.name,
                                        PartialInput = completedTool.input.ToString(),
                                        IsComplete = true
                                    }
                                };
                                currentToolUse.Remove(stopIndex);
                            }
                            break;

                        case "message_stop":
                            yield return new StreamChunk { IsComplete = true };
                            break;
                    }
                }
            }
        }
    }

    private MessageRequest CreateMessageRequest(ChatContext context, IEnumerable<AgentQ.Core.Models.ToolDefinition> tools, bool stream)
    {
        var request = new MessageRequest
        {
            Model = string.IsNullOrEmpty(context.Model) ? DefaultModel : context.Model,
            MaxTokens = context.MaxTokens,
            Messages = ConvertToApiMessages(context.Messages),
            Stream = stream
        };

        if (!string.IsNullOrEmpty(context.SystemPrompt))
        {
            request.System = context.SystemPrompt;
        }

        var toolList = tools.ToList();
        if (toolList.Any())
        {
            request.Tools = toolList.Select(t => new AgentQ.Api.ToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.InputSchema
            }).ToList();
        }

        return request;
    }

    private List<InputMessage> ConvertToApiMessages(List<ChatMessage> messages)
    {
        var result = new List<InputMessage>();

        foreach (var msg in messages)
        {
            var apiMsg = new InputMessage
            {
                Role = msg.Role switch
                {
                    ChatRole.User => "user",
                    ChatRole.Assistant => "assistant",
                    ChatRole.System => "system",
                    ChatRole.Tool => "user",
                    _ => "user"
                },
                Content = new List<InputContentBlock>()
            };

            foreach (var content in msg.Content)
            {
                switch (content.Type)
                {
                    case ContentType.Text:
                        if (!string.IsNullOrEmpty(content.Text))
                            apiMsg.Content.Add(InputContentBlock.CreateText(content.Text));
                        break;

                    case ContentType.ToolUse:
                        if (!string.IsNullOrEmpty(content.ToolId) && !string.IsNullOrEmpty(content.ToolName))
                        {
                            var input = content.ToolInput is JsonElement je
                                ? je
                                : JsonSerializer.SerializeToElement(content.ToolInput);
                            apiMsg.Content.Add(InputContentBlock.CreateToolUse(content.ToolId, content.ToolName, input));
                        }
                        break;

                    case ContentType.ToolResult:
                        if (!string.IsNullOrEmpty(content.ToolUseId))
                        {
                            apiMsg.Content.Add(InputContentBlock.CreateToolResult(
                                content.ToolUseId,
                                content.ToolResult ?? string.Empty,
                                content.IsToolError ?? false));
                        }
                        break;
                }
            }

            if (apiMsg.Content.Any())
            {
                result.Add(apiMsg);
            }
        }

        return result;
    }

    private ChatResponse ConvertToChatResponse(MessageResponse response)
    {
        var chatResponse = new ChatResponse
        {
            Id = response.Id ?? Guid.NewGuid().ToString(),
            Model = response.Model ?? string.Empty,
            Content = new List<ChatContent>()
        };

        if (response.Usage != null)
        {
            chatResponse.Usage = new UsageStats
            {
                InputTokens = (int)response.Usage.InputTokens,
                OutputTokens = (int)response.Usage.OutputTokens
            };
        }

        if (response.Content != null)
        {
            foreach (var block in response.Content)
            {
                switch (block.Type)
                {
                    case OutputContentBlockType.Text:
                        if (!string.IsNullOrEmpty(block.Text))
                            chatResponse.Content.Add(ChatContent.CreateText(block.Text));
                        break;

                    case OutputContentBlockType.ToolUse:
                        if (!string.IsNullOrEmpty(block.Id) && !string.IsNullOrEmpty(block.Name))
                        {
                            chatResponse.Content.Add(ChatContent.CreateToolUse(block.Id, block.Name, block.Input ?? new { }));
                        }
                        break;
                }
            }
        }

        return chatResponse;
    }

    private JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }
}

