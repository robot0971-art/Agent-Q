using System.Net;
using System.Text;
using System.Text.Json;
using AgentQ.Core.Models;
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
}
