using System.Text.Json;
using AgentQ.Cli;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Tools;
using Xunit;

namespace AgentQ.Tests;

/// <summary>
/// CLI 도구 루프 실행기에 대한 단위 테스트 클래스입니다.
/// </summary>
public sealed class CliToolLoopRunnerTests
{
    /// <summary>
    /// ExecuteConversationTurnAsync이 도구 라운드트립을 완료하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteConversationTurnAsync_CompletesToolRoundtrip()
    {
        var provider = new ScriptedProvider(context =>
        {
            var toolResult = context.Messages
                .SelectMany(message => message.Content)
                .FirstOrDefault(content => content.Type == ContentType.ToolResult);

            return toolResult == null
                ? StreamSequence(
                    new StreamChunk { TextDelta = "Checking plugin. " },
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_plugin",
                            ToolName = "plugin_echo",
                            PartialInput = "{\"message\":\"hello from cli\"}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk { IsComplete = true })
                : StreamSequence(
                    new StreamChunk { TextDelta = $"Final: {ExtractPluginMessage(toolResult.ToolResult!)}" },
                    new StreamChunk { IsComplete = true });
        });

        var history = new ChatConversationHistory();
        history.AddUserMessage("run the loop");

        var registry = new ToolRegistry();
        registry.Register(new PluginEchoTool());

        var runner = new CliToolLoopRunner();
        var outputs = new List<string>();

        await runner.ExecuteConversationTurnAsync(
            provider,
            "test-model",
            history,
            registry,
            new AlwaysAllowPermissionEnforcer(),
            onTextDelta: text => outputs.Add(text));

        Assert.Equal(4, history.MessageCount);

        var assistantMessages = history.Messages.Where(message => message.Role == ChatRole.Assistant).ToArray();
        Assert.Equal(2, assistantMessages.Length);
        Assert.Equal("Checking plugin. ", Assert.Single(assistantMessages[0].Content, content => content.Type == ContentType.Text).Text);
        Assert.Equal("plugin_echo", Assert.Single(assistantMessages[0].Content, content => content.Type == ContentType.ToolUse).ToolName);
        Assert.Equal("Final: hello from cli", Assert.Single(assistantMessages[1].Content, content => content.Type == ContentType.Text).Text);

        var toolResultsMessage = Assert.Single(history.Messages, message => message.Role == ChatRole.User && message.Content.Any(content => content.Type == ContentType.ToolResult));
        var toolResult = Assert.Single(toolResultsMessage.Content, content => content.Type == ContentType.ToolResult);
        Assert.False(toolResult.IsToolError);
        Assert.Contains("hello from cli", toolResult.ToolResult);

        Assert.Equal("Checking plugin. Final: hello from cli", string.Concat(outputs));
    }

    /// <summary>
    /// ExecuteConversationTurnAsync이 권한 거부된 도구 결과를 기록하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteConversationTurnAsync_RecordsPermissionDeniedToolResult()
    {
        var provider = new ScriptedProvider(context =>
        {
            var toolResult = context.Messages
                .SelectMany(message => message.Content)
                .FirstOrDefault(content => content.Type == ContentType.ToolResult);

            return toolResult == null
                ? StreamSequence(
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_danger",
                            ToolName = "dangerous_tool",
                            PartialInput = "{\"path\":\"secret.txt\"}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk { IsComplete = true })
                : StreamSequence(
                    new StreamChunk { TextDelta = $"Denied: {toolResult.ToolResult}" },
                    new StreamChunk { IsComplete = true });
        });

        var history = new ChatConversationHistory();
        history.AddUserMessage("run denied flow");

        var registry = new ToolRegistry();
        var dangerousTool = new FakeTool("dangerous_tool", requiresPermission: true, execution: _ => ToolResult.Success("{\"status\":\"unexpected\"}"));
        registry.Register(dangerousTool);

        var runner = new CliToolLoopRunner();
        var deniedTools = new List<string>();

        await runner.ExecuteConversationTurnAsync(
            provider,
            "test-model",
            history,
            registry,
            new FixedPermissionEnforcer(false),
            onPermissionDenied: toolName => deniedTools.Add(toolName));

        Assert.Equal(4, history.MessageCount);
        Assert.Equal(0, dangerousTool.ExecutionCount);
        Assert.Single(deniedTools, "dangerous_tool");

        var toolResultsMessage = Assert.Single(history.Messages, message => message.Role == ChatRole.User && message.Content.Any(content => content.Type == ContentType.ToolResult));
        var toolResult = Assert.Single(toolResultsMessage.Content, content => content.Type == ContentType.ToolResult);
        Assert.True(toolResult.IsToolError);
        Assert.Equal("Permission denied by user", toolResult.ToolResult);

        var finalAssistant = history.Messages.Last();
        Assert.Equal(ChatRole.Assistant, finalAssistant.Role);
        Assert.Equal("Denied: Permission denied by user", Assert.Single(finalAssistant.Content, content => content.Type == ContentType.Text).Text);
    }

    /// <summary>
    /// ExecuteConversationTurnAsync이 최대 step 제한을 초과하면 루프를 중단하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteConversationTurnAsync_StopsWhenMaxStepsExceeded()
    {
        var providerCallCount = 0;
        var provider = new ScriptedProvider(_ =>
        {
            providerCallCount++;
            return StreamSequence(
                new StreamChunk
                {
                    ToolUseDelta = new ToolUseChunk
                    {
                        ToolId = $"tool_{providerCallCount}",
                        ToolName = "plugin_echo",
                        PartialInput = "{\"message\":\"loop\"}",
                        IsComplete = true
                    }
                },
                new StreamChunk { IsComplete = true });
        });

        var history = new ChatConversationHistory();
        history.AddUserMessage("loop forever");

        var registry = new ToolRegistry();
        registry.Register(new PluginEchoTool());

        var runner = new CliToolLoopRunner();

        await runner.ExecuteConversationTurnAsync(
            provider,
            "test-model",
            history,
            registry,
            new AlwaysAllowPermissionEnforcer(),
            maxSteps: 2);

        Assert.Equal(2, providerCallCount);

        var finalAssistant = history.Messages.Last();
        Assert.Equal(ChatRole.Assistant, finalAssistant.Role);
        Assert.Equal(
            "Stopped after reaching the maximum tool steps (2).",
            Assert.Single(finalAssistant.Content, content => content.Type == ContentType.Text).Text);
    }

    /// <summary>
    /// ExecuteConversationTurnAsync이 여러 tool 결과를 섞어서 다음 턴까지 이어가는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteConversationTurnAsync_HandlesMixedMultiToolResults()
    {
        var provider = new ScriptedProvider(context =>
        {
            var toolResults = context.Messages
                .SelectMany(message => message.Content)
                .Where(content => content.Type == ContentType.ToolResult)
                .ToArray();

            return toolResults.Length == 0
                ? StreamSequence(
                    new StreamChunk { TextDelta = "Running tools. " },
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_read",
                            ToolName = "read_file",
                            PartialInput = "{\"path\":\"fixture.txt\"}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_missing",
                            ToolName = "missing_tool",
                            PartialInput = "{}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk { IsComplete = true })
                : StreamSequence(
                    new StreamChunk
                    {
                        TextDelta = string.Join(
                            " | ",
                            toolResults.Select(result => $"{result.ToolUseId}:{result.ToolResult}"))
                    },
                    new StreamChunk { IsComplete = true });
        });

        var history = new ChatConversationHistory();
        history.AddUserMessage("run mixed tools");

        var registry = new ToolRegistry();
        registry.Register(new FakeTool(
            "read_file",
            requiresPermission: false,
            execution: _ => ToolResult.Success("{\"content\":\"fixture parity text\"}")));

        var runner = new CliToolLoopRunner();
        var toolOutputs = new List<string>();

        await runner.ExecuteConversationTurnAsync(
            provider,
            "test-model",
            history,
            registry,
            new AlwaysAllowPermissionEnforcer(),
            onToolOutput: output => toolOutputs.Add(output));

        var toolResultsMessage = Assert.Single(history.Messages, message => message.Role == ChatRole.User && message.Content.Any(content => content.Type == ContentType.ToolResult));
        var toolResults = toolResultsMessage.Content.Where(content => content.Type == ContentType.ToolResult).ToArray();

        Assert.Equal(2, toolResults.Length);
        Assert.Contains(toolResults, result => result.ToolUseId == "tool_read" && result.IsToolError == false);
        Assert.Contains(toolResults, result => result.ToolUseId == "tool_missing" && result.IsToolError == true && result.ToolResult == "Tool not found: missing_tool");
        Assert.Single(toolOutputs, "{\"content\":\"fixture parity text\"}");

        var finalAssistant = history.Messages.Last();
        var finalText = Assert.Single(finalAssistant.Content, content => content.Type == ContentType.Text).Text;
        Assert.Contains("tool_read:{\"content\":\"fixture parity text\"}", finalText);
        Assert.Contains("tool_missing:Tool not found: missing_tool", finalText);
    }

    /// <summary>
    /// ExecuteConversationTurnAsync이 tool 실행 예외를 tool error 결과로 기록하고 다음 턴을 계속하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteConversationTurnAsync_RecordsToolExecutionExceptions_AndContinues()
    {
        var provider = new ScriptedProvider(context =>
        {
            var toolResult = context.Messages
                .SelectMany(message => message.Content)
                .FirstOrDefault(content => content.Type == ContentType.ToolResult);

            return toolResult == null
                ? StreamSequence(
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_broken",
                            ToolName = "broken_tool",
                            PartialInput = "{\"path\":\"fixture.txt\"}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk { IsComplete = true })
                : StreamSequence(
                    new StreamChunk { TextDelta = $"Recovered after {toolResult.ToolResult}" },
                    new StreamChunk { IsComplete = true });
        });

        var history = new ChatConversationHistory();
        history.AddUserMessage("run broken tool");

        var registry = new ToolRegistry();
        registry.Register(new FakeTool(
            "broken_tool",
            requiresPermission: false,
            execution: _ => throw new InvalidOperationException("disk read failed")));

        var runner = new CliToolLoopRunner();
        var toolErrors = new List<string>();

        await runner.ExecuteConversationTurnAsync(
            provider,
            "test-model",
            history,
            registry,
            new AlwaysAllowPermissionEnforcer(),
            onToolError: error => toolErrors.Add(error));

        Assert.Single(toolErrors, "Error: disk read failed");

        var toolResultsMessage = Assert.Single(history.Messages, message => message.Role == ChatRole.User && message.Content.Any(content => content.Type == ContentType.ToolResult));
        var toolResult = Assert.Single(toolResultsMessage.Content, content => content.Type == ContentType.ToolResult);
        Assert.True(toolResult.IsToolError);
        Assert.Equal("Error: disk read failed", toolResult.ToolResult);

        var finalAssistant = history.Messages.Last();
        Assert.Equal(
            "Recovered after Error: disk read failed",
            Assert.Single(finalAssistant.Content, content => content.Type == ContentType.Text).Text);
    }

    /// <summary>
    /// ExecuteConversationTurnAsync이 권한이 필요한 tool을 허용 후 정상 실행하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteConversationTurnAsync_ExecutesPermissionGatedTool_WhenAllowed()
    {
        var provider = new ScriptedProvider(context =>
        {
            var toolResult = context.Messages
                .SelectMany(message => message.Content)
                .FirstOrDefault(content => content.Type == ContentType.ToolResult);

            return toolResult == null
                ? StreamSequence(
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_secure",
                            ToolName = "secure_tool",
                            PartialInput = "{\"path\":\"secret.txt\"}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk { IsComplete = true })
                : StreamSequence(
                    new StreamChunk { TextDelta = $"Executed: {toolResult.ToolResult}" },
                    new StreamChunk { IsComplete = true });
        });

        var history = new ChatConversationHistory();
        history.AddUserMessage("run secure tool");

        var registry = new ToolRegistry();
        var secureTool = new FakeTool(
            "secure_tool",
            requiresPermission: true,
            execution: _ => ToolResult.Success("{\"status\":\"ok\"}"));
        registry.Register(secureTool);

        var runner = new CliToolLoopRunner();

        await runner.ExecuteConversationTurnAsync(
            provider,
            "test-model",
            history,
            registry,
            new FixedPermissionEnforcer(true));

        Assert.Equal(1, secureTool.ExecutionCount);

        var toolResultsMessage = Assert.Single(history.Messages, message => message.Role == ChatRole.User && message.Content.Any(content => content.Type == ContentType.ToolResult));
        var toolResult = Assert.Single(toolResultsMessage.Content, content => content.Type == ContentType.ToolResult);
        Assert.False(toolResult.IsToolError);
        Assert.Equal("{\"status\":\"ok\"}", toolResult.ToolResult);
    }

    /// <summary>
    /// ExecuteConversationTurnAsync이 여러 텍스트 델타를 하나의 assistant 메시지로 합치는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteConversationTurnAsync_AggregatesMultipleTextDeltasIntoSingleAssistantMessage()
    {
        var provider = new ScriptedProvider(_ => StreamSequence(
            new StreamChunk { TextDelta = "Alpha " },
            new StreamChunk { TextDelta = "Beta " },
            new StreamChunk { TextDelta = "Gamma" },
            new StreamChunk { IsComplete = true }));

        var history = new ChatConversationHistory();
        history.AddUserMessage("stream text only");

        var registry = new ToolRegistry();
        var runner = new CliToolLoopRunner();
        var outputs = new List<string>();

        await runner.ExecuteConversationTurnAsync(
            provider,
            "test-model",
            history,
            registry,
            new AlwaysAllowPermissionEnforcer(),
            onTextDelta: text => outputs.Add(text));

        Assert.Equal(2, history.MessageCount);
        var assistant = history.Messages.Last();
        Assert.Equal(ChatRole.Assistant, assistant.Role);
        Assert.Equal("Alpha Beta Gamma", Assert.Single(assistant.Content, content => content.Type == ContentType.Text).Text);
        Assert.Equal("Alpha Beta Gamma", string.Concat(outputs));
    }

    /// <summary>
    /// ExecuteConversationTurnAsync이 같은 턴의 성공/실패 tool 결과를 모두 기록하고 다음 응답으로 이어가는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteConversationTurnAsync_ContinuesAfterMixedToolSuccessAndErrorResults()
    {
        var provider = new ScriptedProvider(context =>
        {
            var toolResults = context.Messages
                .SelectMany(message => message.Content)
                .Where(content => content.Type == ContentType.ToolResult)
                .ToArray();

            return toolResults.Length == 0
                ? StreamSequence(
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_ok",
                            ToolName = "ok_tool",
                            PartialInput = "{\"value\":1}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_fail",
                            ToolName = "fail_tool",
                            PartialInput = "{\"value\":2}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk { IsComplete = true })
                : StreamSequence(
                    new StreamChunk
                    {
                        TextDelta = string.Join(
                            " | ",
                            toolResults.Select(result => $"{result.ToolUseId}:{result.ToolResult}:{result.IsToolError}"))
                    },
                    new StreamChunk { IsComplete = true });
        });

        var history = new ChatConversationHistory();
        history.AddUserMessage("run success and error");

        var registry = new ToolRegistry();
        registry.Register(new FakeTool("ok_tool", requiresPermission: false, execution: _ => ToolResult.Success("{\"status\":\"ok\"}")));
        registry.Register(new FakeTool("fail_tool", requiresPermission: false, execution: _ => ToolResult.Error("simulated failure")));

        var runner = new CliToolLoopRunner();
        var outputs = new List<string>();
        var errors = new List<string>();

        await runner.ExecuteConversationTurnAsync(
            provider,
            "test-model",
            history,
            registry,
            new AlwaysAllowPermissionEnforcer(),
            onToolOutput: output => outputs.Add(output),
            onToolError: error => errors.Add(error));

        Assert.Single(outputs, "{\"status\":\"ok\"}");
        Assert.Single(errors, "simulated failure");

        var toolResultsMessage = Assert.Single(history.Messages, message => message.Role == ChatRole.User && message.Content.Any(content => content.Type == ContentType.ToolResult));
        var toolResults = toolResultsMessage.Content.Where(content => content.Type == ContentType.ToolResult).ToArray();

        Assert.Contains(toolResults, result => result.ToolUseId == "tool_ok" && result.IsToolError == false && result.ToolResult == "{\"status\":\"ok\"}");
        Assert.Contains(toolResults, result => result.ToolUseId == "tool_fail" && result.IsToolError == true && result.ToolResult == "simulated failure");

        var finalAssistant = history.Messages.Last();
        var finalText = Assert.Single(finalAssistant.Content, content => content.Type == ContentType.Text).Text;
        Assert.Contains("tool_ok:{\"status\":\"ok\"}:False", finalText);
        Assert.Contains("tool_fail:simulated failure:True", finalText);
    }

    /// <summary>
    /// ParseJsonArguments가 중첩된 JSON 객체와 배열을 재귀적으로 파싱하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteConversationTurnAsync_ContinuesMixedPermissionOutcomesAcrossTools()
    {
        var provider = new ScriptedProvider(context =>
        {
            var toolResults = context.Messages
                .SelectMany(message => message.Content)
                .Where(content => content.Type == ContentType.ToolResult)
                .ToArray();

            return toolResults.Length == 0
                ? StreamSequence(
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_secure_allowed",
                            ToolName = "secure_tool_allowed",
                            PartialInput = "{\"path\":\"allowed.txt\"}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_secure_denied",
                            ToolName = "secure_tool_denied",
                            PartialInput = "{\"path\":\"denied.txt\"}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk { IsComplete = true })
                : StreamSequence(
                    new StreamChunk
                    {
                        TextDelta = string.Join(
                            " | ",
                            toolResults.Select(result => $"{result.ToolUseId}:{result.ToolResult}"))
                    },
                    new StreamChunk { IsComplete = true });
        });

        var history = new ChatConversationHistory();
        history.AddUserMessage("run mixed secure tools");

        var registry = new ToolRegistry();
        var allowedTool = new FakeTool(
            "secure_tool_allowed",
            requiresPermission: true,
            execution: _ => ToolResult.Success("{\"status\":\"allowed\"}"));
        var deniedTool = new FakeTool(
            "secure_tool_denied",
            requiresPermission: true,
            execution: _ => ToolResult.Success("{\"status\":\"should-not-run\"}"));
        registry.Register(allowedTool);
        registry.Register(deniedTool);

        var runner = new CliToolLoopRunner();
        var deniedTools = new List<string>();

        await runner.ExecuteConversationTurnAsync(
            provider,
            "test-model",
            history,
            registry,
            new PerToolPermissionEnforcer(new Dictionary<string, bool>
            {
                ["secure_tool_allowed"] = true,
                ["secure_tool_denied"] = false
            }),
            onPermissionDenied: toolName => deniedTools.Add(toolName));

        Assert.Equal(1, allowedTool.ExecutionCount);
        Assert.Equal(0, deniedTool.ExecutionCount);
        Assert.Single(deniedTools, "secure_tool_denied");

        var toolResultsMessage = Assert.Single(history.Messages, message => message.Role == ChatRole.User && message.Content.Any(content => content.Type == ContentType.ToolResult));
        var toolResults = toolResultsMessage.Content.Where(content => content.Type == ContentType.ToolResult).ToArray();

        Assert.Contains(toolResults, result => result.ToolUseId == "tool_secure_allowed" && result.IsToolError == false && result.ToolResult == "{\"status\":\"allowed\"}");
        Assert.Contains(toolResults, result => result.ToolUseId == "tool_secure_denied" && result.IsToolError == true && result.ToolResult == "Permission denied by user");
    }

    [Fact]
    public async Task ExecuteConversationTurnAsync_HandlesMalformedToolInputWithoutCrashing()
    {
        Dictionary<string, object?>? capturedInput = null;

        var provider = new ScriptedProvider(context =>
        {
            var toolResult = context.Messages
                .SelectMany(message => message.Content)
                .FirstOrDefault(content => content.Type == ContentType.ToolResult);

            return toolResult == null
                ? StreamSequence(
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_bad_json",
                            ToolName = "inspect_input",
                            PartialInput = "{\"path\":",
                            IsComplete = true
                        }
                    },
                    new StreamChunk { IsComplete = true })
                : StreamSequence(
                    new StreamChunk { TextDelta = $"Recovered: {toolResult.ToolResult}" },
                    new StreamChunk { IsComplete = true });
        });

        var history = new ChatConversationHistory();
        history.AddUserMessage("run malformed tool input");

        var registry = new ToolRegistry();
        registry.Register(new FakeTool(
            "inspect_input",
            requiresPermission: false,
            execution: input =>
            {
                capturedInput = input;
                return ToolResult.Success(JsonSerializer.Serialize(new { count = input.Count }));
            }));

        var runner = new CliToolLoopRunner();

        await runner.ExecuteConversationTurnAsync(
            provider,
            "test-model",
            history,
            registry,
            new AlwaysAllowPermissionEnforcer());

        Assert.NotNull(capturedInput);
        Assert.Empty(capturedInput!);

        var toolResultsMessage = Assert.Single(history.Messages, message => message.Role == ChatRole.User && message.Content.Any(content => content.Type == ContentType.ToolResult));
        var toolResult = Assert.Single(toolResultsMessage.Content, content => content.Type == ContentType.ToolResult);
        Assert.False(toolResult.IsToolError);
        Assert.Equal("{\"count\":0}", toolResult.ToolResult);

        var finalAssistant = history.Messages.Last();
        Assert.Equal("Recovered: {\"count\":0}", Assert.Single(finalAssistant.Content, content => content.Type == ContentType.Text).Text);
    }

    [Fact]
    public void ParseJsonArguments_ParsesNestedObjectsAndArraysRecursively()
    {
        var runner = new CliToolLoopRunner();

        var result = runner.ParseJsonArguments(
            """
            {
              "path": "sample.txt",
              "options": {
                "overwrite": true,
                "retries": 3,
                "tags": ["a", "b"],
                "metadata": {
                  "owner": "agentq",
                  "priority": 1
                }
              }
            }
            """);

        var options = Assert.IsType<Dictionary<string, object?>>(result["options"]);
        Assert.True(Assert.IsType<bool>(options["overwrite"]));
        Assert.Equal(3, Assert.IsType<int>(options["retries"]));

        var tags = Assert.IsType<List<object?>>(options["tags"]);
        Assert.Equal(["a", "b"], tags);

        var metadata = Assert.IsType<Dictionary<string, object?>>(options["metadata"]);
        Assert.Equal("agentq", Assert.IsType<string>(metadata["owner"]));
        Assert.Equal(1, Assert.IsType<int>(metadata["priority"]));
    }

    /// <summary>
    /// StreamChunk 배열을 비동기 열거형으로 반환합니다.
    /// </summary>
    private static async IAsyncEnumerable<StreamChunk> StreamSequence(params StreamChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.Yield();
        }
    }

    /// <summary>
    /// 플러그인 응답 페이로드에서 message 필드를 추출합니다.
    /// </summary>
    private static string ExtractPluginMessage(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    /// <summary>
    /// 스크립팅된 응답을 제공하는 테스트용 LLM 제공자입니다.
    /// </summary>
    private sealed class ScriptedProvider(Func<ChatContext, IAsyncEnumerable<StreamChunk>> streamFactory) : ILlmProvider
    {
        /// <summary>
        /// 제공자 이름입니다.
        /// </summary>
        public string Name => "scripted";

        /// <summary>
        /// 기본 모델 이름입니다.
        /// </summary>
        public string DefaultModel => "scripted-model";

        /// <summary>
        /// 비스트림 응답 생성 (테스트에서 미사용).
        /// </summary>
        public Task<ChatResponse> GenerateResponseAsync(ChatContext context, IEnumerable<ToolDefinition> tools, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// 스트림 응답 생성 (팩토리 함수에 위임).
        /// </summary>
        public IAsyncEnumerable<StreamChunk> GenerateStreamAsync(ChatContext context, IEnumerable<ToolDefinition> tools, CancellationToken ct = default)
        {
            return streamFactory(context);
        }
    }

    /// <summary>
    /// 고정된 권한 응답을 반환하는 테스트용 권한 집행기입니다.
    /// </summary>
    private sealed class FixedPermissionEnforcer(bool allowed) : IPermissionEnforcer
    {
        /// <summary>
        /// 고정된 권한 결과를 반환합니다.
        /// </summary>
        public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson) => Task.FromResult(allowed);
    }

    /// <summary>
    /// 항상 권한을 허용하는 테스트용 권한 집행기입니다.
    /// </summary>
    private sealed class AlwaysAllowPermissionEnforcer : IPermissionEnforcer
    {
        /// <summary>
        /// 항상 true를 반환합니다.
        /// </summary>
        public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson) => Task.FromResult(true);
    }

    /// <summary>
    /// 실행 횟수를 추적할 수 있는 테스트용 도구입니다.
    /// </summary>
    private sealed class PerToolPermissionEnforcer(IReadOnlyDictionary<string, bool> decisions) : IPermissionEnforcer
    {
        public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson) =>
            Task.FromResult(decisions.TryGetValue(toolName, out var allowed) && allowed);
    }

    private sealed class FakeTool(string name, bool requiresPermission, Func<Dictionary<string, object?>, ToolResult> execution) : ITool
    {
        /// <summary>
        /// 도구 실행 횟수입니다.
        /// </summary>
        public int ExecutionCount { get; private set; }

        /// <summary>
        /// 도구 이름입니다.
        /// </summary>
        public string Name => name;

        /// <summary>
        /// 도구 설명입니다.
        /// </summary>
        public string Description => $"{name} description";

        /// <summary>
        /// 도구 입력 스키마입니다.
        /// </summary>
        public object InputSchema => new { type = "object", properties = new { } };

        /// <summary>
        /// 권한 필요 여부입니다.
        /// </summary>
        public bool RequiresPermission => requiresPermission;

        /// <summary>
        /// 도구를 실행하고 횟수를 증가시킵니다.
        /// </summary>
        public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
        {
            ExecutionCount++;
            return Task.FromResult(execution(input));
        }
    }
}

