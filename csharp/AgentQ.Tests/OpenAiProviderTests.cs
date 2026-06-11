using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AgentQ.Core.Models;
using AgentQ.Providers.OpenAi;
using Xunit;

namespace AgentQ.Tests;

/// <summary>
/// OpenAI 호환 제공자에 대한 단위 테스트 클래스입니다.
/// </summary>
public sealed class OpenAiProviderTests
{
    /// <summary>
    /// GenerateResponseAsync가 OpenAI 호환 요청 본문과 인증 헤더를 올바르게 전송하는지 검증합니다.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_SendsExpectedOpenAiCompatibleRequest()
    {
        JsonDocument? capturedRequest = null;
        string? capturedAuthorization = null;

        const string responseBody =
            """
            {
              "id": "chatcmpl_request_check",
              "model": "gpt-4o-mini",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "Request accepted."
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            capturedRequest = JsonDocument.Parse(reader.ReadToEnd());
            capturedAuthorization = request.Headers["Authorization"];
            return new StaticResponse(responseBody, "application/json");
        });

        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "gpt-4o-mini");
        var context = new ChatContext
        {
            Model = "gpt-4o-mini",
            SystemPrompt = "You are a careful assistant.",
            Messages =
            [
                ChatMessage.UserText("Read the file."),
                ChatMessage.AssistantToolUse("call_read", "read_file", new { path = "README.md" }),
                ChatMessage.UserToolResult("call_read", "{\"content\":\"hello\"}", false)
            ],
            MaxTokens = 321
        };

        var response = await provider.GenerateResponseAsync(context, CreateToolDefinitions("read_file"));

        Assert.Equal("Request accepted.", Assert.Single(response.Content).Text);
        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer test-key", capturedAuthorization);

        var root = capturedRequest!.RootElement;
        Assert.Equal("gpt-4o-mini", root.GetProperty("model").GetString());
        Assert.Equal(321, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());

        var tools = root.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Single(tools);
        Assert.Equal("read_file", tools[0].GetProperty("function").GetProperty("name").GetString());

        var messages = root.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(4, messages.Length);
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are a careful assistant.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Read the file.", messages[1].GetProperty("content").GetString());
        Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
        Assert.Equal("function", messages[2].GetProperty("tool_calls")[0].GetProperty("type").GetString());
        Assert.Equal("read_file", messages[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("{\"path\":\"README.md\"}", messages[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal("tool", messages[3].GetProperty("role").GetString());
        Assert.Equal("call_read", messages[3].GetProperty("tool_call_id").GetString());
        Assert.Equal("{\"content\":\"hello\"}", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_ClampsHugeMaxTokens()
    {
        JsonDocument? capturedRequest = null;
        const string responseBody =
            """
            {
              "id": "chatcmpl_max_tokens",
              "model": "gpt-4o-mini",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "ok"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            capturedRequest = JsonDocument.Parse(reader.ReadToEnd());
            return new StaticResponse(responseBody, "application/json");
        });

        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "gpt-4o-mini");
        var context = new ChatContext
        {
            Model = "gpt-4o-mini",
            Messages = [ChatMessage.UserText("hello")],
            MaxTokens = uint.MaxValue
        };

        await provider.GenerateResponseAsync(context, []);

        Assert.NotNull(capturedRequest);
        Assert.Equal(int.MaxValue, capturedRequest!.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_PreservesBaseUrlPathWithoutTrailingSlash()
    {
        string? capturedPath = null;

        const string responseBody =
            """
            {
              "id": "chatcmpl_path_check",
              "model": "kimi-k2.6",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "ok"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(request =>
        {
            capturedPath = request.Url?.AbsolutePath;
            return new StaticResponse(responseBody, "application/json");
        }, "v1");

        var provider = new OpenAiCompatibleProvider(server.BaseUrl.TrimEnd('/'), "test-key", "kimi-k2.6");

        await provider.GenerateResponseAsync(CreateContext(), []);

        Assert.Equal("/v1/chat/completions", capturedPath);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_ExpandsMultipleToolResultsIntoToolMessages()
    {
        JsonDocument? capturedRequest = null;

        const string responseBody =
            """
            {
              "id": "chatcmpl_multi_tool_result_check",
              "model": "kimi-k2.6",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "done"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            capturedRequest = JsonDocument.Parse(reader.ReadToEnd());
            return new StaticResponse(responseBody, "application/json");
        });

        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "kimi-k2.6");
        var context = new ChatContext
        {
            Model = "kimi-k2.6",
            Messages =
            [
                ChatMessage.UserText("Run commands."),
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content =
                    [
                        ChatContent.CreateToolUse("bash:0", "bash", "{\"command\":\"pwd\"}"),
                        ChatContent.CreateToolUse("bash:1", "bash", "{\"command\":\"ls\"}")
                    ]
                },
                new ChatMessage
                {
                    Role = ChatRole.User,
                    Content =
                    [
                        ChatContent.CreateToolResult("bash:0", "{\"stdout\":\"/tmp\"}", false),
                        ChatContent.CreateToolResult("bash:1", "{\"stdout\":\"README.md\"}", false)
                    ]
                }
            ]
        };

        await provider.GenerateResponseAsync(context, CreateToolDefinitions("bash"));

        Assert.NotNull(capturedRequest);
        var messages = capturedRequest!.RootElement.GetProperty("messages").EnumerateArray().ToArray();

        Assert.Equal(4, messages.Length);
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("bash:0", messages[1].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("bash:1", messages[1].GetProperty("tool_calls")[1].GetProperty("id").GetString());
        Assert.Equal("tool", messages[2].GetProperty("role").GetString());
        Assert.Equal("bash:0", messages[2].GetProperty("tool_call_id").GetString());
        Assert.Equal("{\"stdout\":\"/tmp\"}", messages[2].GetProperty("content").GetString());
        Assert.Equal("tool", messages[3].GetProperty("role").GetString());
        Assert.Equal("bash:1", messages[3].GetProperty("tool_call_id").GetString());
        Assert.Equal("{\"stdout\":\"README.md\"}", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_AddsReasoningContentPlaceholderForOpenCodeGoKimiToolCalls()
    {
        JsonDocument? capturedRequest = null;

        const string responseBody =
            """
            {
              "id": "chatcmpl_reasoning_placeholder_check",
              "model": "kimi-k2.6",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "done"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            capturedRequest = JsonDocument.Parse(reader.ReadToEnd());
            return new StaticResponse(responseBody, "application/json");
        });

        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "kimi-k2.6", "opencode-go");
        var context = new ChatContext
        {
            Model = "kimi-k2.6",
            Messages =
            [
                ChatMessage.UserText("Run commands."),
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content =
                    [
                        ChatContent.CreateToolUse("bash:0", "bash", "{\"command\":\"pwd\"}")
                    ]
                },
                new ChatMessage
                {
                    Role = ChatRole.User,
                    Content =
                    [
                        ChatContent.CreateToolResult("bash:0", "{\"stdout\":\"/tmp\"}", false)
                    ]
                }
            ]
        };

        await provider.GenerateResponseAsync(context, CreateToolDefinitions("bash"));

        Assert.NotNull(capturedRequest);
        var messages = capturedRequest!.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal(" ", messages[1].GetProperty("reasoning_content").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_PreservesReasoningContentForOpenCodeGoDeepSeekToolCalls()
    {
        JsonDocument? capturedRequest = null;

        const string responseBody =
            """
            {
              "id": "chatcmpl_deepseek_reasoning_check",
              "model": "deepseek-v3.2",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "done"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            capturedRequest = JsonDocument.Parse(reader.ReadToEnd());
            return new StaticResponse(responseBody, "application/json");
        });

        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "deepseek-v3.2", "opencode-go");
        var context = new ChatContext
        {
            Model = "deepseek-v3.2",
            Messages =
            [
                ChatMessage.UserText("Run commands."),
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content =
                    [
                        new ChatContent
                        {
                            Type = ContentType.ToolUse,
                            ToolId = "bash:0",
                            ToolName = "bash",
                            ToolInput = "{\"command\":\"pwd\"}",
                            ReasoningContent = "internal reasoning"
                        }
                    ]
                },
                new ChatMessage
                {
                    Role = ChatRole.User,
                    Content =
                    [
                        ChatContent.CreateToolResult("bash:0", "{\"stdout\":\"/tmp\"}", false)
                    ]
                }
            ]
        };

        await provider.GenerateResponseAsync(context, CreateToolDefinitions("bash"));

        Assert.NotNull(capturedRequest);
        var messages = capturedRequest!.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("internal reasoning", messages[1].GetProperty("reasoning_content").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_DisablesThinkingForOpenCodeGoKimiModels()
    {
        JsonDocument? capturedRequest = null;

        const string responseBody =
            """
            {
              "id": "chatcmpl_thinking_check",
              "model": "kimi-k2.6",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "ok"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            capturedRequest = JsonDocument.Parse(reader.ReadToEnd());
            return new StaticResponse(responseBody, "application/json");
        });

        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "kimi-k2.6", "opencode-go");
        var context = CreateContext();
        context.Model = "kimi-k2.6";

        await provider.GenerateResponseAsync(context, CreateToolDefinitions("bash"));

        Assert.NotNull(capturedRequest);
        var thinking = capturedRequest!.RootElement.GetProperty("thinking");
        Assert.Equal("disabled", thinking.GetProperty("type").GetString());
    }


    /// <summary>
    /// GenerateResponseAsync가 도구 호출과 사용량을 올바르게 파싱하는지 검증합니다.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
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
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_ParsesObjectToolArgumentsFromCompatibleProviders()
    {
        const string responseBody =
            """
            {
              "id": "chatcmpl_object_args",
              "model": "compatible-model",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "tool_calls": [
                      {
                        "id": "call_read",
                        "type": "function",
                        "function": {
                          "name": "read_file",
                          "arguments": { "path": "fixture.txt" }
                        }
                      }
                    ]
                  },
                  "finish_reason": "tool_calls"
                }
              ]
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(_ => new StaticResponse(responseBody, "application/json"));
        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "compatible-model");

        var response = await provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file"));

        var toolUse = Assert.Single(response.Content, content => content.Type == ContentType.ToolUse);
        Assert.Equal("call_read", toolUse.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
        Assert.Equal("{ \"path\": \"fixture.txt\" }", toolUse.ToolInput);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_ReplacesWhitespaceToolCallId()
    {
        const string responseBody =
            """
            {
              "id": "chatcmpl_blank_tool_id",
              "model": "compatible-model",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "tool_calls": [
                      {
                        "id": "   ",
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

        await using var server = await OpenAiTestServer.StartAsync(_ => new StaticResponse(responseBody, "application/json"));
        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "compatible-model");

        var response = await provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file"));

        var toolUse = Assert.Single(response.Content, content => content.Type == ContentType.ToolUse);
        Assert.False(string.IsNullOrWhiteSpace(toolUse.ToolId));
        Assert.NotEqual("   ", toolUse.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_DropsToolCallsWithoutToolName()
    {
        const string responseBody =
            """
            {
              "id": "chatcmpl_missing_tool_name",
              "model": "compatible-model",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "I tried to call a tool.",
                    "tool_calls": [
                      {
                        "id": "call_missing_name",
                        "type": "function",
                        "function": {
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

        await using var server = await OpenAiTestServer.StartAsync(_ => new StaticResponse(responseBody, "application/json"));
        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "compatible-model");

        var response = await provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file"));

        Assert.DoesNotContain(response.Content, content => content.Type == ContentType.ToolUse);
        Assert.Equal("I tried to call a tool.", Assert.Single(response.Content, content => content.Type == ContentType.Text).Text);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_ParsesLegacyFunctionCall()
    {
        const string responseBody =
            """
            {
              "id": "chatcmpl_legacy_function",
              "model": "compatible-model",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "I will inspect the file.",
                    "function_call": {
                      "name": "read_file",
                      "arguments": "{\"path\":\"fixture.txt\"}"
                    }
                  },
                  "finish_reason": "function_call"
                }
              ]
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(_ => new StaticResponse(responseBody, "application/json"));
        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "compatible-model");

        var response = await provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file"));

        Assert.Equal("I will inspect the file.", Assert.Single(response.Content, content => content.Type == ContentType.Text).Text);
        var toolUse = Assert.Single(response.Content, content => content.Type == ContentType.ToolUse);
        Assert.Equal("function_call_0", toolUse.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", Assert.IsType<string>(toolUse.ToolInput));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateResponseAsync_UsesLegacyFunctionCallWhenToolCallsAreInvalid()
    {
        const string responseBody =
            """
            {
              "id": "chatcmpl_legacy_fallback",
              "model": "compatible-model",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "tool_calls": [
                      {
                        "id": "call_missing_name",
                        "type": "function",
                        "function": {
                          "arguments": "{\"path\":\"bad.txt\"}"
                        }
                      }
                    ],
                    "function_call": {
                      "name": "read_file",
                      "arguments": "{\"path\":\"fixture.txt\"}"
                    }
                  },
                  "finish_reason": "function_call"
                }
              ]
            }
            """;

        await using var server = await OpenAiTestServer.StartAsync(_ => new StaticResponse(responseBody, "application/json"));
        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "compatible-model");

        var response = await provider.GenerateResponseAsync(CreateContext(), CreateToolDefinitions("read_file"));

        var toolUse = Assert.Single(response.Content, content => content.Type == ContentType.ToolUse);
        Assert.Equal("function_call_0", toolUse.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", Assert.IsType<string>(toolUse.ToolInput));
    }

    /// <summary>
    /// GenerateStreamAsync이 스트리밍 응답에서 여러 도구 호출을 올바르게 조립하는지 검증합니다.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
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

        Assert.Single(chunks, chunk => chunk.IsComplete);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateStreamAsync_AssemblesLegacyFunctionCall()
    {
        const string streamBody =
            """
            data: {"id":"chatcmpl_legacy_stream","choices":[{"index":0,"delta":{"content":"Working."},"finish_reason":null}]}

            data: {"id":"chatcmpl_legacy_stream","choices":[{"index":0,"delta":{"function_call":{"name":"read_file","arguments":"{\"path\":\"fi"}},"finish_reason":null}]}

            data: {"id":"chatcmpl_legacy_stream","choices":[{"index":0,"delta":{"function_call":{"arguments":"xture.txt\"}"}},"finish_reason":null}]}

            data: {"id":"chatcmpl_legacy_stream","choices":[{"index":0,"delta":{},"finish_reason":"function_call"}]}

            data: [DONE]

            """;

        await using var server = await OpenAiTestServer.StartAsync(_ => new StaticResponse(streamBody, "text/event-stream"));
        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "compatible-model");

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.GenerateStreamAsync(CreateContext(), CreateToolDefinitions("read_file")))
        {
            chunks.Add(chunk);
        }

        Assert.Equal("Working.", string.Concat(chunks.Select(chunk => chunk.TextDelta).Where(value => !string.IsNullOrEmpty(value))));
        var toolUse = Assert.Single(chunks.Where(chunk => chunk.ToolUseDelta?.IsComplete == true).Select(chunk => chunk.ToolUseDelta!));
        Assert.Equal("tool_call_0", toolUse.ToolId);
        Assert.Equal("read_file", toolUse.ToolName);
        Assert.Equal("{\"path\":\"fixture.txt\"}", toolUse.PartialInput);
        Assert.Single(chunks, chunk => chunk.IsComplete);
    }

    /// <summary>
    /// GenerateStreamAsync가 스트리밍 요청에서 stream=true를 전송하는지 검증합니다.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateStreamAsync_SendsStreamEnabledRequest()
    {
        JsonDocument? capturedRequest = null;

        const string streamBody =
            """
            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{"content":"ok"},"finish_reason":null}]}

            data: {"id":"chatcmpl_stream","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;

        await using var server = await OpenAiTestServer.StartAsync(request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            capturedRequest = JsonDocument.Parse(reader.ReadToEnd());
            return new StaticResponse(streamBody, "text/event-stream");
        });

        var provider = new OpenAiCompatibleProvider(server.BaseUrl, "test-key", "gpt-4o-mini");
        var context = CreateContext();

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.GenerateStreamAsync(context, CreateToolDefinitions("read_file")))
        {
            chunks.Add(chunk);
        }

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("gpt-4o-mini", capturedRequest.RootElement.GetProperty("model").GetString());
        Assert.Contains(chunks, chunk => chunk.TextDelta == "ok");
        Assert.Single(chunks, chunk => chunk.IsComplete);
    }

    /// <summary>
    /// 테스트용 채팅 컨텍스트를 생성합니다.
    /// </summary>
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

    /// <summary>
    /// 지정된 이름으로 도구 정의 배열을 생성합니다.
    /// </summary>
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

    /// <summary>
    /// 테스트용 OpenAI 호환 HTTP 서버입니다.
    /// </summary>
    private sealed class OpenAiTestServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<OpenAiTestRequest, StaticResponse> _responseFactory;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _listenerTask;

        /// <summary>
        /// 지정된 접두사와 응답 팩토리로 테스트 서버를 생성합니다.
        /// </summary>
        private OpenAiTestServer(string prefix, int port, Func<OpenAiTestRequest, StaticResponse> responseFactory)
        {
            BaseUrl = prefix.TrimEnd('/');
            _responseFactory = responseFactory;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _listenerTask = Task.Run(ListenLoopAsync);
        }

        /// <summary>
        /// 서버의 기본 URL입니다.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// 지정된 응답 팩토리로 테스트 서버를 시작합니다.
        /// </summary>
        public static Task<OpenAiTestServer> StartAsync(Func<OpenAiTestRequest, StaticResponse> responseFactory, string? pathPrefix = null)
        {
            var (prefix, port) = BuildListenerPrefix(pathPrefix);
            return Task.FromResult(new OpenAiTestServer(prefix, port, responseFactory));
        }

        /// <summary>
        /// 테스트 서버를 정리하고 리소스를 해제합니다.
        /// </summary>
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
            _cts.Dispose();
        }

        /// <summary>
        /// 들어오는 HTTP 요청을 처리하는 루프입니다.
        /// </summary>
        private async Task ListenLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                using (client)
                {
                    var stream = client.GetStream();
                    var request = await OpenAiTestRequest.ReadAsync(stream, BaseUrl, _cts.Token);
                    var response = _responseFactory(request);
                    var bytes = Encoding.UTF8.GetBytes(response.Body);
                    var headers =
                        "HTTP/1.1 200 OK\r\n" +
                        $"Content-Type: {response.ContentType}\r\n" +
                        $"Content-Length: {bytes.Length}\r\n" +
                        "Connection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), _cts.Token);
                    await stream.WriteAsync(bytes, _cts.Token);
                }
            }
        }

        /// <summary>
        /// 사용 가능한 포트로 리스너 접두사를 생성합니다.
        /// </summary>
        private static (string Prefix, int Port) BuildListenerPrefix(string? pathPrefix = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            var normalizedPath = string.IsNullOrWhiteSpace(pathPrefix)
                ? string.Empty
                : $"{pathPrefix.Trim('/')}/";
            return ($"http://127.0.0.1:{port}/{normalizedPath}", port);
        }
    }

    private sealed class OpenAiTestRequest
    {
        public required Stream InputStream { get; init; }

        public Encoding ContentEncoding { get; init; } = Encoding.UTF8;

        public required WebHeaderCollection Headers { get; init; }

        public required Uri Url { get; init; }

        public static async Task<OpenAiTestRequest> ReadAsync(Stream stream, string baseUrl, CancellationToken ct)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(ct) ?? "POST / HTTP/1.1";
            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var path = parts.Length > 1 ? parts[1] : "/";
            var headers = new WebHeaderCollection();
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct)))
            {
                var separator = line.IndexOf(':');
                if (separator > 0)
                {
                    headers[line[..separator]] = line[(separator + 1)..].Trim();
                }
            }

            var contentLength = int.TryParse(headers["Content-Length"], out var parsedLength) ? parsedLength : 0;
            var buffer = new char[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(read, contentLength - read), ct);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            var body = Encoding.UTF8.GetBytes(new string(buffer, 0, read));
            return new OpenAiTestRequest
            {
                InputStream = new MemoryStream(body),
                Headers = headers,
                Url = new Uri("http://127.0.0.1" + path)
            };
        }
    }

    /// <summary>
    /// 정적 응답을 나타내는 레코드입니다.
    /// </summary>
    private sealed record StaticResponse(string Body, string ContentType);
}

