using System.Net;
using System.Net.Sockets;
using System.Text;
using AgentQ.Core.Models;
using AgentQ.Providers.OpenAi;
using Xunit;

namespace AgentQ.Tests;

public sealed class OpenAiProviderTests
{
    [Fact]
    public async Task GenerateResponseAsync_ParsesToolCallsAndUsage()
    {
        const string responseBody =
            """
            {
              "id": "chatcmpl_test",
              "model": "gpt-4o-mini",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "I will use tools.",
                    "tool_calls": [
                      {
                        "id": "call_read",
                        "type": "function",
                        "function": {
                          "name": "read_file",
                          "arguments": "{\"path\":\"fixture.txt\"}"
                        }
                      },
                      {
                        "id": "call_grep",
                        "type": "function",
                        "function": {
                          "name": "grep_search",
                          "arguments": "{\"pattern\":\"parity\"}"
                        }
                      }
                    ]
                  },
                  "finish_reason": "tool_calls"
                }
              ],
              "usage": {
                "prompt_tokens": 12,
                "completion_tokens": 5,
                "total_tokens": 17
              }
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(_ => new StaticResponse(responseBody, "application/json"));
        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "gpt-4o-mini");
        var context = CreateContext();

        var response = await provider.GenerateResponseAsync(context, CreateToolDefinitions("read_file", "grep_search"));

        Assert.Equal("chatcmpl_test", response.Id);
        Assert.Equal("gpt-4o-mini", response.Model);
        Assert.Equal(12, response.Usage?.InputTokens);
        Assert.Equal(5, response.Usage?.OutputTokens);
        Assert.Equal("I will use tools.", Assert.Single(response.Content, c => c.Type == ContentType.Text).Text);

        var toolUses = response.Content.Where(c => c.Type == ContentType.ToolUse).ToArray();
        Assert.Equal(2, toolUses.Length);
        Assert.Contains(toolUses, tool => tool.ToolId == "call_read" && tool.ToolName == "read_file");
        Assert.Contains(toolUses, tool => tool.ToolId == "call_grep" && tool.ToolName == "grep_search");
    }

    [Fact]
    public async Task GenerateStreamAsync_AssemblesMultipleToolCalls()
    {
        const string streamBody =
            """
            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"content":"Working "},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"content":"through tools."},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_read","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"fi"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"xture.txt\"}"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"tool_calls":[{"index":1,"id":"call_grep","type":"function","function":{"name":"grep_search","arguments":"{\"pattern\":\"pa"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"tool_calls":[{"index":1,"function":{"arguments":"rity\",\"path\":\"fixture.txt\"}"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;

        await using var server = await OpenAiTestServer.StartAsync(_ => new StaticResponse(streamBody, "text/event-stream"));
        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "gpt-4o-mini");
        var context = CreateContext();

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.GenerateStreamAsync(context, CreateToolDefinitions("read_file", "grep_search")))
        {
            chunks.Add(chunk);
        }

        var text = string.Concat(chunks.Select(chunk => chunk.TextDelta).Where(value => !string.IsNullOrEmpty(value)));
        Assert.Equal("Working through tools.", text);

        var toolUses = chunks
            .Where(chunk => chunk.ToolUseDelta?.IsComplete == true)
            .Select(chunk => chunk.ToolUseDelta!)
            .ToArray();

        Assert.Equal(2, toolUses.Length);
        Assert.Contains(toolUses, tool => tool.ToolId == "call_read" &&
                                          tool.ToolName == "read_file" &&
                                          tool.PartialInput == "{\"path\":\"fixture.txt\"}");
        Assert.Contains(toolUses, tool => tool.ToolId == "call_grep" &&
                                          tool.ToolName == "grep_search" &&
                                          tool.PartialInput == "{\"pattern\":\"parity\",\"path\":\"fixture.txt\"}");

        Assert.Equal(2, chunks.Count(chunk => chunk.IsComplete));
    }

    private static ChatContext CreateContext()
    {
        return new ChatContext
        {
            Model = "gpt-4o-mini",
            Messages = new List<ChatMessage>
            {
                ChatMessage.UserText("Run the tool flow.")
            },
            MaxTokens = 512
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

    private sealed class OpenAiTestServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Func<HttpListenerRequest, StaticResponse> _responseFactory;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _listenerTask;

        private OpenAiTestServer(string prefix, Func<HttpListenerRequest, StaticResponse> responseFactory)
        {
            BaseUrl = prefix.TrimEnd('/');
            _responseFactory = responseFactory;
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _listenerTask = Task.Run(ListenLoopAsync);
        }

        public string BaseUrl { get; }

        public static Task<OpenAiTestServer> StartAsync(Func<HttpListenerRequest, StaticResponse> responseFactory)
        {
            var prefix = BuildListenerPrefix();
            return Task.FromResult(new OpenAiTestServer(prefix, responseFactory));
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();

            try
            {
                await _listenerTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (HttpListenerException)
            {
            }

            _listener.Close();
            _cts.Dispose();
        }

        private async Task ListenLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                var response = _responseFactory(context.Request);
                var bytes = Encoding.UTF8.GetBytes(response.Body);
                context.Response.StatusCode = 200;
                context.Response.ContentType = response.ContentType;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        private static string BuildListenerPrefix()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return $"http://127.0.0.1:{port}/";
        }
    }

    private sealed record StaticResponse(string Body, string ContentType);
}

