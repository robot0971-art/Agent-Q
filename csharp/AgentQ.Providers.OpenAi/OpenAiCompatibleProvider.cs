using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;

namespace AgentQ.Providers.OpenAi;

/// <summary>
/// OpenAI 호환 LLM 제공자
/// </summary>
public class OpenAiCompatibleProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _name;

    /// <summary>
    /// 제공자 이름
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// 기본 모델
    /// </summary>
    public string DefaultModel => _model;

    /// <summary>
    /// 생성자
    /// </summary>
    /// <param name="baseUrl">기본 URL</param>
    /// <param name="apiKey">API 키</param>
    /// <param name="model">모델 이름</param>
    public OpenAiCompatibleProvider(string baseUrl, string apiKey, string model = "gpt-4o", string name = "openai")
        : this(ProviderHttpClientFactory.Shared, baseUrl, apiKey, model, name)
    {
    }

    public OpenAiCompatibleProvider(
        IProviderHttpClientFactory httpClientFactory,
        string baseUrl,
        string apiKey,
        string model = "gpt-4o",
        string name = "openai")
    {
        _model = model;
        _name = name;
        _httpClient = httpClientFactory.CreateClient(baseUrl, apiKey, ProviderHttpAuthKind.Bearer);
    }

    /// <summary>
    /// 테스트 또는 커스텀 전송 파이프라인용 생성자
    /// </summary>
    /// <param name="httpClient">사용할 HTTP 클라이언트</param>
    /// <param name="model">모델 이름</param>
    public OpenAiCompatibleProvider(HttpClient httpClient, string model = "gpt-4o", string name = "openai")
    {
        _httpClient = httpClient;
        _model = model;
        _name = name;
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
        var request = CreateChatRequest(context, tools, false);
        var json = JsonSerializer.Serialize(request, GetJsonOptions());

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat/completions", content, ct);
        await EnsureSuccessStatusCodeAsync(response, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var chatResponse = JsonSerializer.Deserialize<OpenAiChatResponse>(body, GetJsonOptions());

        return ConvertToChatResponse(chatResponse!);
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
        var request = CreateChatRequest(context, tools, true);
        var json = JsonSerializer.Serialize(request, GetJsonOptions());

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var requestMsg = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = content };
        using var response = await _httpClient.SendAsync(requestMsg, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessStatusCodeAsync(response, ct);

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var toolCallBuffer = new ToolCallDeltaBuffer();
        var completionSent = false;

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
                foreach (var toolCall in toolCallBuffer.CompleteAll())
                {
                    yield return new StreamChunk
                    {
                        ToolUseDelta = toolCall
                    };
                }

                if (!completionSent)
                {
                    yield return new StreamChunk { IsComplete = true };
                }

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
                if (chunk?.Usage != null)
                {
                    yield return new StreamChunk
                    {
                        Usage = new UsageStats
                        {
                            InputTokens = chunk.Usage.PromptTokens,
                            OutputTokens = chunk.Usage.CompletionTokens
                        }
                    };
                }

                continue;
            }

            foreach (var choice in chunk.Choices)
            {
                var delta = choice.Delta;

                if (!string.IsNullOrEmpty(delta?.Content))
                {
                    yield return new StreamChunk { TextDelta = delta.Content };
                }

                if (!string.IsNullOrEmpty(delta?.ReasoningContent))
                {
                    yield return new StreamChunk { ReasoningDelta = delta.ReasoningContent };
                }

                if (delta?.ToolCalls != null)
                {
                    foreach (var toolCall in delta.ToolCalls)
                    {
                        toolCallBuffer.SetToolId(toolCall.Index, toolCall.Id);
                        toolCallBuffer.SetToolName(toolCall.Index, toolCall.Function?.Name);
                        toolCallBuffer.AppendArguments(toolCall.Index, toolCall.Function?.Arguments);
                    }
                }

                if (choice.FinishReason == "tool_calls")
                {
                    foreach (var toolCall in toolCallBuffer.CompleteAll())
                    {
                        yield return new StreamChunk
                        {
                            ToolUseDelta = toolCall
                        };
                    }
                }

                if (IsTerminalFinishReason(choice.FinishReason) && !completionSent)
                {
                    completionSent = true;
                    yield return new StreamChunk { IsComplete = true };
                }
            }
        }
    }

    /// <summary>
    /// 채팅 요청 생성
    /// </summary>
    private OpenAiChatRequest CreateChatRequest(ChatContext context, IEnumerable<AgentQ.Core.Models.ToolDefinition> tools, bool stream)
    {
        var messages = new List<OpenAiMessage>();
        var effectiveModel = string.IsNullOrEmpty(context.Model) ? DefaultModel : context.Model;

        if (!string.IsNullOrEmpty(context.SystemPrompt))
        {
            messages.Add(new OpenAiMessage { Role = "system", Content = context.SystemPrompt });
        }

        foreach (var msg in context.Messages)
        {
            if (msg.Content.Any(c => c.Type == ContentType.ToolResult))
            {
                foreach (var toolResult in msg.Content.Where(c => c.Type == ContentType.ToolResult))
                {
                    messages.Add(new OpenAiMessage
                    {
                        Role = "tool",
                        ToolCallId = toolResult.ToolUseId,
                        Content = toolResult.ToolResult
                    });
                }

                continue;
            }

            var role = msg.Role switch
            {
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.System => "system",
                ChatRole.Tool => "tool",
                _ => "user"
            };

            var openAiMsg = new OpenAiMessage { Role = role };

            if (msg.Content.Any(c => c.Type == ContentType.Image))
            {
                openAiMsg.Content = msg.Content
                    .Where(c => c.Type is ContentType.Text or ContentType.Image)
                    .Select(ToOpenAiContentPart)
                    .ToList();
            }
            else if (msg.Content.Count == 1 && msg.Content[0].Type == ContentType.Text)
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

                var reasoningContent = toolUses
                    .Select(t => t.ReasoningContent)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (!string.IsNullOrWhiteSpace(reasoningContent) &&
                    ShouldSendReasoningContent(effectiveModel))
                {
                    openAiMsg.ReasoningContent = reasoningContent;
                }
                else if (ShouldApplyKimiToolCallCompatibility(effectiveModel))
                {
                    openAiMsg.ReasoningContent = " ";
                }

                var textContent = msg.Content.Where(c => c.Type == ContentType.Text).Select(c => c.Text).FirstOrDefault();
                if (!string.IsNullOrEmpty(textContent))
                {
                    openAiMsg.Content = textContent;
                }
            }
            else
            {
                openAiMsg.Content = string.Join("\n", msg.Content.Where(c => c.Type == ContentType.Text).Select(c => c.Text));
            }

            messages.Add(openAiMsg);
        }

        var request = new OpenAiChatRequest
        {
            Model = effectiveModel,
            Messages = messages,
            MaxTokens = context.MaxTokens == 0 ? 1024 : (int)context.MaxTokens,
            Stream = stream,
            StreamOptions = stream ? new OpenAiStreamOptions { IncludeUsage = true } : null
        };

        if (ShouldDisableThinking(request.Model))
        {
            request.Thinking = new OpenAiThinkingOptions { Type = "disabled" };
        }

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

    /// <summary>
    /// 채팅 응답으로 변환
    /// </summary>
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

            var content = ExtractMessageText(choice.Message?.Content);
            if (!string.IsNullOrEmpty(content))
            {
                chatResponse.Content.Add(ChatContent.CreateText(content));
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

    /// <summary>
    /// 도구 인수 직렬화
    /// </summary>
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

    private static OpenAiContentPart ToOpenAiContentPart(ChatContent content)
    {
        if (content.Type == ContentType.Image)
        {
            return new OpenAiContentPart
            {
                Type = "image_url",
                ImageUrl = new OpenAiImageUrl
                {
                    Url = $"data:{content.MediaType};base64,{content.Base64Data}"
                }
            };
        }

        return new OpenAiContentPart
        {
            Type = "text",
            Text = content.Text ?? string.Empty
        };
    }

    private static string? ExtractMessageText(object? content)
    {
        return content switch
        {
            null => null,
            string text => text,
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString(),
            JsonElement json when json.ValueKind == JsonValueKind.Array => string.Join(
                "\n",
                json.EnumerateArray()
                    .Where(part => part.TryGetProperty("type", out var type) &&
                                   type.GetString() == "text" &&
                                   part.TryGetProperty("text", out _))
                    .Select(part => part.GetProperty("text").GetString())
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => content.ToString()
        };
    }

    /// <summary>
    /// JSON 옵션 가져오기
    /// </summary>
    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private bool ShouldDisableThinking(string model) =>
        _name.Equals("opencode-go", StringComparison.OrdinalIgnoreCase) &&
        model.StartsWith("kimi-", StringComparison.OrdinalIgnoreCase);

    private bool ShouldApplyKimiToolCallCompatibility(string model) =>
        _name.Equals("opencode-go", StringComparison.OrdinalIgnoreCase) &&
        model.StartsWith("kimi-", StringComparison.OrdinalIgnoreCase);

    private bool ShouldSendReasoningContent(string model) =>
        !(_name.Equals("opencode-go", StringComparison.OrdinalIgnoreCase) &&
          model.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase));

    private static bool IsTerminalFinishReason(string? finishReason)
    {
        return finishReason is "stop" or "length" or "content_filter";
    }

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
            $"OpenAI-compatible request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {trimmedBody}",
            null,
            response.StatusCode);
    }
}

/// <summary>
/// OpenAI 채팅 요청
/// </summary>
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

    [JsonPropertyName("stream_options")]
    public OpenAiStreamOptions? StreamOptions { get; set; }

    [JsonPropertyName("thinking")]
    public OpenAiThinkingOptions? Thinking { get; set; }
}

public class OpenAiStreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool IncludeUsage { get; set; }
}

public class OpenAiThinkingOptions
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// OpenAI 메시지
/// </summary>
public class OpenAiMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public object? Content { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }
}

public class OpenAiContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    public OpenAiImageUrl? ImageUrl { get; set; }
}

public class OpenAiImageUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// OpenAI 도구 정의
/// </summary>
public class OpenAiToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAiFunctionDefinition Function { get; set; } = new();
}

/// <summary>
/// OpenAI 함수 정의
/// </summary>
public class OpenAiFunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public object Parameters { get; set; } = new();
}

/// <summary>
/// OpenAI 채팅 응답
/// </summary>
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

/// <summary>
/// OpenAI 선택
/// </summary>
public class OpenAiChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

/// <summary>
/// OpenAI 사용량
/// </summary>
public class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

/// <summary>
/// OpenAI 스트리밍 청크
/// </summary>
public class OpenAiStreamChunk
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenAiStreamChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }
}

/// <summary>
/// OpenAI 스트리밍 선택
/// </summary>
public class OpenAiStreamChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public OpenAiStreamDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

/// <summary>
/// OpenAI 스트리밍 델타
/// </summary>
public class OpenAiStreamDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; set; }
}

/// <summary>
/// OpenAI 도구 호출
/// </summary>
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

/// <summary>
/// OpenAI 함수 호출
/// </summary>
public class OpenAiFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

