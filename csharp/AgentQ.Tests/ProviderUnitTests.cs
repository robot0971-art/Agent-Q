using System.Net;
using System.Text;
using System.Text.Json;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Providers.Anthropic;
using AgentQ.Providers.OpenAi;
using Xunit;

namespace AgentQ.Tests;

/// <summary>
/// HTTP 리스너 없이 실행 가능한 provider 단위 테스트입니다.
/// </summary>
public sealed class ProviderUnitTests
{
    [Fact]
    public async Task ResilientProvider_DoesNotRetryClientHttpErrors()
    {
        var inner = new FailingProvider(new HttpRequestException("bad request", null, HttpStatusCode.BadRequest));
        var provider = new ResilientLlmProvider(inner, maxRetries: 3, initialDelay: TimeSpan.Zero);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file")));

        Assert.Equal(1, inner.ResponseAttempts);
    }

    [Fact]
    public async Task ResilientProvider_RetriesRateLimitHttpErrors()
    {
        var inner = new EventuallySuccessfulProvider(
            failuresBeforeSuccess: 2,
            new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests));
        var provider = new ResilientLlmProvider(inner, maxRetries: 3, initialDelay: TimeSpan.Zero);

        var response = await provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file"));

        Assert.Equal("ok", response.Id);
        Assert.Equal(3, inner.ResponseAttempts);
    }

    [Fact]
    public async Task ResilientProvider_ReportsRetryAttempts()
    {
        var retries = new List<LlmProviderRetryInfo>();
        var inner = new EventuallySuccessfulProvider(
            failuresBeforeSuccess: 1,
            new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests));
        var provider = new ResilientLlmProvider(
            inner,
            maxRetries: 3,
            initialDelay: TimeSpan.Zero,
            onRetry: retries.Add);

        var response = await provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file"));

        Assert.Equal("ok", response.Id);
        var retry = Assert.Single(retries);
        Assert.Equal("eventual", retry.ProviderName);
        Assert.Equal(1, retry.Attempt);
        Assert.Equal(3, retry.MaxRetries);
        Assert.Equal(HttpStatusCode.TooManyRequests, retry.StatusCode);
        Assert.Contains("rate limited", retry.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolCallDeltaBuffer_CompleteAllHandlesMultiplePendingCalls()
    {
        var buffer = new ToolCallDeltaBuffer();
        buffer.SetToolId(1, "call_second");
        buffer.SetToolName(1, "write_file");
        buffer.AppendArguments(1, "{\"path\":\"b.txt\"}");
        buffer.SetToolId(0, "call_first");
        buffer.SetToolName(0, "read_file");
        buffer.AppendArguments(0, "{\"path\":\"a.txt\"}");

        var completed = buffer.CompleteAll();

        Assert.Collection(
            completed,
            first =>
            {
                Assert.Equal("call_first", first.ToolId);
                Assert.Equal("read_file", first.ToolName);
                Assert.Equal("{\"path\":\"a.txt\"}", first.PartialInput);
                Assert.True(first.IsComplete);
            },
            second =>
            {
                Assert.Equal("call_second", second.ToolId);
                Assert.Equal("write_file", second.ToolName);
                Assert.Equal("{\"path\":\"b.txt\"}", second.PartialInput);
                Assert.True(second.IsComplete);
            });
        Assert.Empty(buffer.CompleteAll());
    }

    [Fact]
    public void ToolCallDeltaBuffer_CompletesToolCallsWithoutProviderId()
    {
        var buffer = new ToolCallDeltaBuffer();
        buffer.SetToolName(0, "read_file");
        buffer.AppendArguments(0, "{\"path\":\"fixture.txt\"}");

        var completed = Assert.Single(buffer.CompleteAll());

        Assert.Equal("tool_call_0", completed.ToolId);
        Assert.Equal("read_file", completed.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", completed.PartialInput);
        Assert.True(completed.IsComplete);
    }

    [Fact]
    public void ToolCallDeltaBuffer_DropsToolCallsWithoutToolName()
    {
        var buffer = new ToolCallDeltaBuffer();
        buffer.SetToolId(0, "call_missing_name");
        buffer.AppendArguments(0, "{\"path\":\"fixture.txt\"}");

        Assert.Null(buffer.BuildPartialChunk(0));
        Assert.Empty(buffer.CompleteAll());
    }

    [Fact]
    public void ToolCallDeltaBuffer_DropsToolCallsWithWhitespaceToolName()
    {
        var buffer = new ToolCallDeltaBuffer();
        buffer.SetToolId(0, "call_blank_name");
        buffer.SetToolName(0, "   ");
        buffer.AppendArguments(0, "{\"path\":\"fixture.txt\"}");

        Assert.Null(buffer.BuildPartialChunk(0));
        Assert.Empty(buffer.CompleteAll());
    }

    [Fact]
    public void ToolCallDeltaBuffer_UsesFallbackIdForWhitespaceProviderId()
    {
        var buffer = new ToolCallDeltaBuffer();
        buffer.SetToolId(0, "   ");
        buffer.SetToolName(0, "read_file");
        buffer.AppendArguments(0, "{\"path\":\"fixture.txt\"}");

        var completed = Assert.Single(buffer.CompleteAll());

        Assert.Equal("tool_call_0", completed.ToolId);
        Assert.Equal("read_file", completed.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", completed.PartialInput);
    }

    [Fact]
    public async Task OpenAiStream_IgnoresMalformedChunks_AndCompletesBufferedToolCalls()
    {
        const string body =
            """
            data: not-json

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"content":"Working "},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_read","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"fi"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"xture.txt\"}"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;

        using var httpClient = CreateHttpClient(HttpStatusCode.OK, body, "text/event-stream");
        var provider = new OpenAiCompatibleProvider(httpClient, "gpt-4o-mini");

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.GenerateStreamAsync(CreateContext(), CreateToolDefinitions("read_file")))
        {
            chunks.Add(chunk);
        }

        Assert.Equal("Working ", string.Concat(chunks.Select(chunk => chunk.TextDelta)));

        var toolUseChunk = Assert.Single(chunks, chunk => chunk.ToolUseDelta?.IsComplete == true);
        var toolUse = Assert.IsType<ToolUseChunk>(toolUseChunk.ToolUseDelta);
        Assert.Equal("call_read", toolUse!.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", toolUse.PartialInput);
        Assert.Single(chunks, chunk => chunk.IsComplete);
    }

    [Fact]
    public async Task OpenAiStream_CompletesToolCallWhenCompatibleProviderOmitsId()
    {
        const string body =
            """
            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"type":"function","function":{"name":"read_file","arguments":"{\"path\":\"fi"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"xture.txt\"}"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

            data: [DONE]

            """;

        using var httpClient = CreateHttpClient(HttpStatusCode.OK, body, "text/event-stream");
        var provider = new OpenAiCompatibleProvider(httpClient, "compatible-model");

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.GenerateStreamAsync(CreateContext(), CreateToolDefinitions("read_file")))
        {
            chunks.Add(chunk);
        }

        var toolUseChunk = Assert.Single(chunks, chunk => chunk.ToolUseDelta?.IsComplete == true);
        var toolUse = Assert.IsType<ToolUseChunk>(toolUseChunk.ToolUseDelta);
        Assert.Equal("tool_call_0", toolUse!.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", toolUse.PartialInput);
        Assert.Single(chunks, chunk => chunk.IsComplete);
    }

    [Fact]
    public async Task OpenAiStream_HandlesMultiLineDataEvents()
    {
        const string body =
            """
            data: {"id":"chatcmpl_stream","choices":[{"index":0,
            data: "delta":{"tool_calls":[{"index":0,"id":"call_read","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"fi"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,
            data: "function":{"arguments":"xture.txt\"}"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

            data: [DONE]

            """;

        using var httpClient = CreateHttpClient(HttpStatusCode.OK, body, "text/event-stream");
        var provider = new OpenAiCompatibleProvider(httpClient, "gpt-4o-mini");

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.GenerateStreamAsync(CreateContext(), CreateToolDefinitions("read_file")))
        {
            chunks.Add(chunk);
        }

        var toolUseChunk = Assert.Single(chunks, chunk => chunk.ToolUseDelta?.IsComplete == true);
        var toolUse = Assert.IsType<ToolUseChunk>(toolUseChunk.ToolUseDelta);
        Assert.Equal("call_read", toolUse.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", toolUse.PartialInput);
        Assert.Single(chunks, chunk => chunk.IsComplete);
    }

    [Fact]
    public async Task OpenAiResponse_ThrowsHelpfulHttpError()
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.BadRequest, "{\"error\":\"bad request\"}");
        var provider = new OpenAiCompatibleProvider(httpClient, "gpt-4o-mini");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file")));

        Assert.Contains("400", error.Message);
        Assert.Contains("bad request", error.Message);
    }

    [Fact]
    public async Task OpenAiResponse_HandlesMissingUsage_AndToolCalls()
    {
        const string body =
            """
            {
              "id": "chatcmpl_test",
              "model": "gpt-4o-mini",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "I will inspect the file.",
                    "tool_calls": [
                      {
                        "id": "call_123",
                        "type": "function",
                        "function": {
                          "name": "read_file",
                          "arguments": "{\"path\":\"fixture.txt\"}"
                        }
                      }
                    ]
                  },
                  "finish_reason": "tool_calls"
                }
              ]
            }
            """;

        using var httpClient = CreateHttpClient(HttpStatusCode.OK, body);
        var provider = new OpenAiCompatibleProvider(httpClient, "gpt-4o-mini");

        var response = await provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file"));

        Assert.Equal("chatcmpl_test", response.Id);
        Assert.Equal("gpt-4o-mini", response.Model);
        Assert.Null(response.Usage);
        Assert.Equal("I will inspect the file.", Assert.Single(response.Content, content => content.Type == ContentType.Text).Text);

        var toolUse = Assert.Single(response.Content, content => content.Type == ContentType.ToolUse);
        Assert.Equal("call_123", toolUse.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", Assert.IsType<string>(toolUse.ToolInput));
    }

    [Fact]
    public async Task AnthropicStream_IgnoresMalformedEvents_AndCompletesPendingToolCall()
    {
        const string body =
            """
            event: content_block_start
            data: {"index":9,"content_block":{"type":"tool_use","id":"tool_bad","name":{"not":"a string"}}}

            event: content_block_stop
            data: {"index":9}

            event: content_block_start
            data: {"index":0,"content_block":{"type":"tool_use","id":"tool_1","name":"read_file"}}

            event: content_block_delta
            data: {"index":0,"delta":{"type":"input_json_delta","partial_json":"{\"path\":\"fi"}}

            event: content_block_delta
            data: not-json

            event: content_block_delta
            data: {"index":0,"delta":{"type":"input_json_delta","partial_json":"xture.txt\"}"}}

            event: message_stop
            data: {}

            """;

        using var httpClient = CreateHttpClient(HttpStatusCode.OK, body, "text/event-stream");
        var provider = new AnthropicProvider(httpClient, "test-key");

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.GenerateStreamAsync(CreateContext(), CreateToolDefinitions("read_file")))
        {
            chunks.Add(chunk);
        }

        var toolUseChunk = Assert.Single(chunks, chunk => chunk.ToolUseDelta?.IsComplete == true);
        var toolUse = Assert.IsType<ToolUseChunk>(toolUseChunk.ToolUseDelta);
        Assert.Equal("tool_1", toolUse!.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", toolUse.PartialInput);
        Assert.Single(chunks, chunk => chunk.IsComplete);
    }

    [Fact]
    public async Task AnthropicStream_HandlesCommentsAndMultiLineDataEvents()
    {
        const string body =
            """
            event: content_block_start
            : keep-alive
            data: {"index":0,"content_block":
            data: {"type":"tool_use","id":"tool_1","name":"read_file"}}

            event: content_block_delta
            data: {"index":0,"delta":{"type":"input_json_delta","partial_json":"{\"path\":\"fi"}}

            event: content_block_delta
            : another heartbeat
            data: {"index":0,"delta":
            data: {"type":"input_json_delta","partial_json":"xture.txt\"}"}}

            event: content_block_stop
            data: {"index":0}

            event: message_stop
            data: {}

            """;

        using var httpClient = CreateHttpClient(HttpStatusCode.OK, body, "text/event-stream");
        var provider = new AnthropicProvider(httpClient, "test-key");

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.GenerateStreamAsync(CreateContext(), CreateToolDefinitions("read_file")))
        {
            chunks.Add(chunk);
        }

        var toolUse = Assert.Single(chunks.Where(chunk => chunk.ToolUseDelta?.IsComplete == true).Select(chunk => chunk.ToolUseDelta!));
        Assert.Equal("tool_1", toolUse.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", toolUse.PartialInput);
        Assert.Single(chunks, chunk => chunk.IsComplete);
    }

    [Fact]
    public async Task AnthropicResponse_ThrowsHelpfulHttpError()
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"rate limited\"}}");
        var provider = new AnthropicProvider(httpClient, "test-key");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file")));

        Assert.Contains("429", error.Message);
        Assert.Contains("rate limited", error.Message);
    }

    [Fact]
    public async Task AnthropicResponse_HandlesMissingUsage_AndStructuredToolInput()
    {
        const string body =
            """
            {
              "id": "msg_test",
              "model": "claude-sonnet-4-6",
              "role": "assistant",
              "content": [
                {
                  "type": "text",
                  "text": "Let me inspect that."
                },
                {
                  "type": "tool_use",
                  "id": "tool_123",
                  "name": "read_file",
                  "input": {
                    "path": "fixture.txt",
                    "options": {
                      "include_hidden": true
                    }
                  }
                }
              ]
            }
            """;

        using var httpClient = CreateHttpClient(HttpStatusCode.OK, body);
        var provider = new AnthropicProvider(httpClient, "test-key");

        var response = await provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file"));

        Assert.Equal("msg_test", response.Id);
        Assert.Equal("claude-sonnet-4-6", response.Model);
        Assert.Null(response.Usage);
        Assert.Equal("Let me inspect that.", Assert.Single(response.Content, content => content.Type == ContentType.Text).Text);

        var toolUse = Assert.Single(response.Content, content => content.Type == ContentType.ToolUse);
        Assert.Equal("tool_123", toolUse.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);

        var input = Assert.IsType<JsonElement>(toolUse.ToolInput);
        Assert.Equal("fixture.txt", input.GetProperty("path").GetString());
        Assert.True(input.GetProperty("options").GetProperty("include_hidden").GetBoolean());
    }

    [Fact]
    public async Task AnthropicResponse_DropsWhitespaceToolMetadata()
    {
        const string body =
            """
            {
              "id": "msg_blank_tool",
              "model": "claude-sonnet-4-6",
              "role": "assistant",
              "content": [
                {
                  "type": "tool_use",
                  "id": "   ",
                  "name": "read_file",
                  "input": {
                    "path": "fixture.txt"
                  }
                },
                {
                  "type": "tool_use",
                  "id": "tool_123",
                  "name": "   ",
                  "input": {
                    "path": "fixture.txt"
                  }
                }
              ]
            }
            """;

        using var httpClient = CreateHttpClient(HttpStatusCode.OK, body);
        var provider = new AnthropicProvider(httpClient, "test-key");

        var response = await provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file"));

        Assert.DoesNotContain(response.Content, content => content.Type == ContentType.ToolUse);
    }

    [Fact]
    public async Task AnthropicRequest_SendsStringifiedToolInputAsObject()
    {
        const string responseBody =
            """
            {
              "id": "msg_test",
              "model": "claude-sonnet-4-6",
              "role": "assistant",
              "content": [
                {
                  "type": "text",
                  "text": "done"
                }
              ]
            }
            """;

        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var provider = new AnthropicProvider(httpClient, "test-key");
        var context = new ChatContext
        {
            Model = "claude-sonnet-4-6",
            Messages = new List<ChatMessage>
            {
                ChatMessage.UserText("Read the file."),
                ChatMessage.AssistantToolUse("tool_123", "read_file", "{\"path\":\"fixture.txt\"}"),
                ChatMessage.UserToolResult("tool_123", "{\"contents\":\"ok\"}", false)
            },
            MaxTokens = 256
        };

        await provider.GenerateResponseAsync(context, CreateToolDefinitions("read_file"));

        Assert.False(string.IsNullOrWhiteSpace(capturedBody));
        using var doc = JsonDocument.Parse(capturedBody!);
        var messages = doc.RootElement.GetProperty("messages");
        var toolUse = messages[1].GetProperty("content")[0];
        Assert.Equal("tool_use", toolUse.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, toolUse.GetProperty("input").ValueKind);
        Assert.Equal("fixture.txt", toolUse.GetProperty("input").GetProperty("path").GetString());
    }

    [Fact]
    public async Task AnthropicRequest_DefaultsZeroMaxTokens()
    {
        const string responseBody =
            """
            {
              "id": "msg_tokens",
              "model": "claude-sonnet-4-6",
              "role": "assistant",
              "content": [
                {
                  "type": "text",
                  "text": "ok"
                }
              ]
            }
            """;

        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };
        var provider = new AnthropicProvider(httpClient, "test-key");
        var context = new ChatContext
        {
            Model = "claude-sonnet-4-6",
            Messages = [ChatMessage.UserText("hello")],
            MaxTokens = 0
        };

        await provider.GenerateResponseAsync(context, []);

        Assert.False(string.IsNullOrWhiteSpace(capturedBody));
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal(1024u, doc.RootElement.GetProperty("max_tokens").GetUInt32());
    }

    private static ChatContext CreateContext()
    {
        return new ChatContext
        {
            Model = "test-model",
            Messages = new List<ChatMessage>
            {
                ChatMessage.UserText("Run provider flow.")
            },
            MaxTokens = 256
        };
    }

    private static ToolDefinition[] CreateToolDefinitions(params string[] names)
    {
        return names.Select(name => new ToolDefinition
        {
            Name = name,
            Description = $"{name} test tool",
            InputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object?>()
            }
        }).ToArray();
    }

    private static HttpClient CreateHttpClient(HttpStatusCode statusCode, string body, string contentType = "application/json")
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            });

        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class FailingProvider(Exception error) : ILlmProvider
    {
        public int ResponseAttempts { get; private set; }

        public string Name => "failing";

        public string DefaultModel => "test";

        public Task<ChatResponse> GenerateResponseAsync(
            ChatContext context,
            IEnumerable<ToolDefinition> tools,
            CancellationToken ct = default)
        {
            ResponseAttempts++;
            throw error;
        }

        public async IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
            ChatContext context,
            IEnumerable<ToolDefinition> tools,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            throw error;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class EventuallySuccessfulProvider(int failuresBeforeSuccess, Exception error) : ILlmProvider
    {
        public int ResponseAttempts { get; private set; }

        public string Name => "eventual";

        public string DefaultModel => "test";

        public Task<ChatResponse> GenerateResponseAsync(
            ChatContext context,
            IEnumerable<ToolDefinition> tools,
            CancellationToken ct = default)
        {
            ResponseAttempts++;
            if (ResponseAttempts <= failuresBeforeSuccess)
            {
                throw error;
            }

            return Task.FromResult(new ChatResponse
            {
                Id = "ok",
                Model = "test",
                Content = [ChatContent.CreateText("ok")]
            });
        }

        public async IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
            ChatContext context,
            IEnumerable<ToolDefinition> tools,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new StreamChunk { TextDelta = "ok" };
            yield return new StreamChunk { IsComplete = true };
        }
    }
}
