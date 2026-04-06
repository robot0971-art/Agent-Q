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
    /// 실행 횟수를 추적할 수 있는 테스트용 도구입니다.
    /// </summary>
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

