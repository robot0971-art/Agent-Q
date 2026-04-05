using System.Text.Json;
using AgentQ.Cli;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Tools;
using Xunit;

namespace AgentQ.Tests;

public sealed class CliToolLoopRunnerTests
{
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

    private static async IAsyncEnumerable<StreamChunk> StreamSequence(params StreamChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.Yield();
        }
    }

    private static string ExtractPluginMessage(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    private sealed class ScriptedProvider(Func<ChatContext, IAsyncEnumerable<StreamChunk>> streamFactory) : ILlmProvider
    {
        public string Name => "scripted";
        public string DefaultModel => "scripted-model";

        public Task<ChatResponse> GenerateResponseAsync(ChatContext context, IEnumerable<ToolDefinition> tools, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<StreamChunk> GenerateStreamAsync(ChatContext context, IEnumerable<ToolDefinition> tools, CancellationToken ct = default)
        {
            return streamFactory(context);
        }
    }

    private sealed class FixedPermissionEnforcer(bool allowed) : IPermissionEnforcer
    {
        public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson) => Task.FromResult(allowed);
    }

    private sealed class FakeTool(string name, bool requiresPermission, Func<Dictionary<string, object?>, ToolResult> execution) : ITool
    {
        public int ExecutionCount { get; private set; }

        public string Name => name;
        public string Description => $"{name} description";
        public object InputSchema => new { type = "object", properties = new { } };
        public bool RequiresPermission => requiresPermission;

        public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
        {
            ExecutionCount++;
            return Task.FromResult(execution(input));
        }
    }
}

