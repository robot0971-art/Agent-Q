using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using AgentQ.Api;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;

namespace AgentQ.Providers.Anthropic;

/// <summary>
/// Anthropic LLM 제공자
/// </summary>
public class AnthropicProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    /// <summary>
    /// 제공자 이름
    /// </summary>
    public string Name => "anthropic";

    /// <summary>
    /// 기본 모델
    /// </summary>
    public string DefaultModel => "claude-sonnet-4-6";

    /// <summary>
    /// 생성자
    /// </summary>
    /// <param name="baseUrl">기본 URL</param>
    /// <param name="apiKey">API 키</param>
    public AnthropicProvider(string baseUrl, string apiKey)
        : this(ProviderHttpClientFactory.Shared, baseUrl, apiKey)
    {
    }

    public AnthropicProvider(IProviderHttpClientFactory httpClientFactory, string baseUrl, string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = httpClientFactory.CreateClient(baseUrl, apiKey, ProviderHttpAuthKind.Anthropic);
    }

    /// <summary>
    /// 테스트 또는 커스텀 전송 파이프라인용 생성자
    /// </summary>
    /// <param name="httpClient">사용할 HTTP 클라이언트</param>
    /// <param name="apiKey">API 키</param>
    public AnthropicProvider(HttpClient httpClient, string apiKey = "")
    {
        _apiKey = apiKey;
        _httpClient = httpClient;

        if (!string.IsNullOrEmpty(apiKey) && !_httpClient.DefaultRequestHeaders.Contains("x-api-key"))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("anthropic-version"))
        {
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
    }

    /// <summary>
    /// 응답 생성
    /// </summary>
    /// <param name="context">채팅 컨텍스트</param>
    /// <param name="tools">사용 가능한 도구 목록</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>채팅 응답</returns>
    public async Task<ChatResponse> GenerateResponseAsync(
        ChatContext context,
        IEnumerable<AgentQ.Core.Models.ToolDefinition> tools,
        CancellationToken ct = default)
    {
        var request = CreateMessageRequest(context, tools, false);
        var json = JsonSerializer.Serialize(request, GetJsonOptions());

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("/v1/messages", content, ct);
        await EnsureSuccessStatusCodeAsync(response, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var messageResponse = JsonSerializer.Deserialize<MessageResponse>(body, GetJsonOptions());

        return ConvertToChatResponse(messageResponse!);
    }

    /// <summary>
    /// 스트리밍 응답 생성
    /// </summary>
    /// <param name="context">채팅 컨텍스트</param>
    /// <param name="tools">사용 가능한 도구 목록</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>스트리밍 청크 시퀀스</returns>
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
        await EnsureSuccessStatusCodeAsync(response, ct);

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var toolCallBuffer = new ToolCallDeltaBuffer();
        var completionSent = false;
        UsageStats? latestUsage = null;
        var usageSent = false;
        var eventType = string.Empty;
        var eventData = new StringBuilder();

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (line.Length == 0)
            {
                foreach (var chunk in ProcessAnthropicStreamEvent(
                    eventType,
                    eventData.ToString(),
                    toolCallBuffer,
                    ref completionSent,
                    ref latestUsage,
                    ref usageSent))
                {
                    yield return chunk;
                }

                eventType = string.Empty;
                eventData.Clear();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventType = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (eventData.Length > 0)
                {
                    eventData.AppendLine();
                }

                eventData.Append(line["data:".Length..].Trim());
            }
        }

        foreach (var chunk in ProcessAnthropicStreamEvent(
            eventType,
            eventData.ToString(),
            toolCallBuffer,
            ref completionSent,
            ref latestUsage,
            ref usageSent))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 메시지 요청 생성
    /// </summary>
    /// <param name="context">채팅 컨텍스트</param>
    /// <param name="tools">도구 목록</param>
    /// <param name="stream">스트리밍 여부</param>
    /// <returns>메시지 요청</returns>
    private MessageRequest CreateMessageRequest(ChatContext context, IEnumerable<AgentQ.Core.Models.ToolDefinition> tools, bool stream)
    {
        var request = new MessageRequest
        {
            Model = string.IsNullOrEmpty(context.Model) ? DefaultModel : context.Model,
            MaxTokens = NormalizeMaxTokens(context.MaxTokens),
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

    /// <summary>
    /// API 메시지로 변환
    /// </summary>
    /// <param name="messages">채팅 메시지 목록</param>
    /// <returns>API 메시지 목록</returns>
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
                        if (!string.IsNullOrWhiteSpace(content.ToolId) && !string.IsNullOrWhiteSpace(content.ToolName))
                        {
                            var input = NormalizeToolInput(content.ToolInput);
                            apiMsg.Content.Add(InputContentBlock.CreateToolUse(content.ToolId, content.ToolName, input));
                        }
                        break;

                    case ContentType.ToolResult:
                        if (!string.IsNullOrWhiteSpace(content.ToolUseId))
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

    private static object NormalizeToolInput(object? toolInput)
    {
        if (toolInput is JsonElement jsonElement)
        {
            return jsonElement;
        }

        if (toolInput is string rawInput)
        {
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                return EmptyToolInput();
            }

            try
            {
                using var doc = JsonDocument.Parse(rawInput);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    ? doc.RootElement.Clone()
                    : EmptyToolInput();
            }
            catch (JsonException)
            {
                return EmptyToolInput();
            }
        }

        if (toolInput == null)
        {
            return EmptyToolInput();
        }

        var serialized = JsonSerializer.SerializeToElement(toolInput);
        return serialized.ValueKind == JsonValueKind.Object
            ? serialized
            : EmptyToolInput();
    }

    private static JsonElement EmptyToolInput()
    {
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }

    /// <summary>
    /// 채팅 응답으로 변환
    /// </summary>
    /// <param name="response">메시지 응답</param>
    /// <returns>채팅 응답</returns>
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
                InputTokens = ToInt32TokenCount((ulong)response.Usage.InputTokens + response.Usage.CacheCreationInputTokens + response.Usage.CacheReadInputTokens),
                OutputTokens = ToInt32TokenCount(response.Usage.OutputTokens)
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
                        if (!string.IsNullOrWhiteSpace(block.Id) && !string.IsNullOrWhiteSpace(block.Name))
                        {
                            chatResponse.Content.Add(ChatContent.CreateToolUse(block.Id, block.Name, block.Input ?? new { }));
                        }
                        break;
                }
            }
        }

        return chatResponse;
    }

    /// <summary>
    /// JSON 옵션 가져오기
    /// </summary>
    /// <returns>JSON 직렬화 옵션</returns>
    private JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out value);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static uint NormalizeMaxTokens(uint maxTokens)
    {
        return maxTokens == 0 ? 1024u : maxTokens;
    }

    private static IReadOnlyList<StreamChunk> ProcessAnthropicStreamEvent(
        string eventType,
        string data,
        ToolCallDeltaBuffer toolCallBuffer,
        ref bool completionSent,
        ref UsageStats? latestUsage,
        ref bool usageSent)
    {
        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(data))
        {
            return [];
        }

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(data);
        }
        catch (JsonException)
        {
            return [];
        }

        using (doc)
        {
            var chunks = new List<StreamChunk>();
            var root = doc.RootElement;

            switch (eventType)
            {
                case "message_start":
                    if (root.TryGetProperty("message", out var messageStart) &&
                        TryReadAnthropicUsage(messageStart, out var startUsage))
                    {
                        latestUsage = startUsage;
                    }

                    break;

                case "message_delta":
                    if (TryReadAnthropicUsage(root, out var deltaUsage))
                    {
                        latestUsage = MergeUsage(latestUsage, deltaUsage);
                    }

                    break;

                case "content_block_start":
                    if (!TryGetInt32(root, "index", out var startIndex) ||
                        !root.TryGetProperty("content_block", out var contentBlock) ||
                        !TryGetString(contentBlock, "type", out var startType))
                    {
                        break;
                    }

                    if (startType == "tool_use")
                    {
                        toolCallBuffer.SetToolId(startIndex, TryGetString(contentBlock, "id", out var id) ? id : null);
                        toolCallBuffer.SetToolName(startIndex, TryGetString(contentBlock, "name", out var name) ? name : null);
                        var partialChunk = toolCallBuffer.BuildPartialChunk(startIndex, "");
                        if (partialChunk != null)
                        {
                            chunks.Add(new StreamChunk { ToolUseDelta = partialChunk });
                        }
                    }

                    break;

                case "content_block_delta":
                    if (!TryGetInt32(root, "index", out var deltaIndex) ||
                        !root.TryGetProperty("delta", out var delta) ||
                        !TryGetString(delta, "type", out var deltaType))
                    {
                        break;
                    }

                    if (deltaType == "text_delta")
                    {
                        if (TryGetString(delta, "text", out var text) && !string.IsNullOrEmpty(text))
                        {
                            chunks.Add(new StreamChunk { TextDelta = text });
                        }
                    }
                    else if (deltaType == "input_json_delta")
                    {
                        var partialJson = TryGetString(delta, "partial_json", out var jsonDelta) ? jsonDelta : string.Empty;
                        toolCallBuffer.AppendArguments(deltaIndex, partialJson);
                        var partialChunk = toolCallBuffer.BuildPartialChunk(deltaIndex, partialJson);
                        if (partialChunk != null)
                        {
                            chunks.Add(new StreamChunk { ToolUseDelta = partialChunk });
                        }
                    }

                    break;

                case "content_block_stop":
                    if (!TryGetInt32(root, "index", out var stopIndex))
                    {
                        break;
                    }

                    var completedChunk = toolCallBuffer.Complete(stopIndex);
                    if (completedChunk != null)
                    {
                        chunks.Add(new StreamChunk { ToolUseDelta = completedChunk });
                    }

                    break;

                case "message_stop":
                    foreach (var toolCall in toolCallBuffer.CompleteAll())
                    {
                        chunks.Add(new StreamChunk { ToolUseDelta = toolCall });
                    }

                    if (!usageSent && latestUsage != null)
                    {
                        usageSent = true;
                        chunks.Add(new StreamChunk { Usage = latestUsage });
                    }

                    if (!completionSent)
                    {
                        completionSent = true;
                        chunks.Add(new StreamChunk { IsComplete = true });
                    }

                    break;
            }

            return chunks;
        }
    }

    private static bool TryReadAnthropicUsage(JsonElement root, out UsageStats usage)
    {
        usage = new UsageStats();
        if (!root.TryGetProperty("usage", out var usageElement) ||
            usageElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var inputTokens = GetUInt64Property(usageElement, "input_tokens") +
                          GetUInt64Property(usageElement, "cache_creation_input_tokens") +
                          GetUInt64Property(usageElement, "cache_read_input_tokens");
        var outputTokens = GetUInt64Property(usageElement, "output_tokens");

        usage = new UsageStats
        {
            InputTokens = ToInt32TokenCount(inputTokens),
            OutputTokens = ToInt32TokenCount(outputTokens)
        };
        return inputTokens > 0 || outputTokens > 0;
    }

    private static UsageStats MergeUsage(UsageStats? current, UsageStats next)
    {
        if (current == null)
        {
            return next;
        }

        return new UsageStats
        {
            InputTokens = next.InputTokens > 0 ? next.InputTokens : current.InputTokens,
            OutputTokens = next.OutputTokens > 0 ? next.OutputTokens : current.OutputTokens
        };
    }

    private static ulong GetUInt64Property(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.TryGetUInt64(out var uintValue))
        {
            return uintValue;
        }

        return property.TryGetInt64(out var intValue) && intValue > 0
            ? (ulong)intValue
            : 0;
    }

    private static int ToInt32TokenCount(ulong count) =>
        count > int.MaxValue ? int.MaxValue : (int)count;

    private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var trimmedBody = string.IsNullOrWhiteSpace(body)
            ? "<empty body>"
            : body.Length > 512
                ? body[..512]
                : body;

        throw new HttpRequestException(
            $"Anthropic request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {trimmedBody}",
            null,
            response.StatusCode);
    }
}
