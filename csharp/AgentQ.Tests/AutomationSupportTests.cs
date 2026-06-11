using System.Text.Json;
using AgentQ.Cli;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Tools;
using Xunit;

namespace AgentQ.Tests;

public sealed class AutomationSupportTests
{
    [Fact]
    public async Task NonInteractivePermissionEnforcer_AllowsOnlyExplicitlyPermittedTools()
    {
        var enforcer = new NonInteractivePermissionEnforcer(
            allowToolsWithoutPrompt: false,
            allowedToolNames: ["read_file", "grep_search"]);

        Assert.True(await enforcer.RequestPermissionAsync("read_file", "read", "{}"));
        Assert.True(await enforcer.RequestPermissionAsync("grep_search", "grep", "{}"));
        Assert.False(await enforcer.RequestPermissionAsync("bash", "shell", "{}"));
    }

    [Fact]
    public async Task NonInteractivePermissionEnforcer_DenyListOverridesAllowRules()
    {
        var enforcer = new NonInteractivePermissionEnforcer(
            allowToolsWithoutPrompt: true,
            allowedToolNames: ["read_file"],
            deniedToolNames: ["bash", "read_file"]);

        Assert.False(await enforcer.RequestPermissionAsync("bash", "shell", "{}"));
        Assert.False(await enforcer.RequestPermissionAsync("read_file", "read", "{}"));
    }

    [Fact]
    public async Task ResolveInvocationAsync_RejectsMultiplePromptSources()
    {
        var config = new ProviderConfiguration
        {
            Prompt = "hello",
            ReadPromptFromStdin = true
        };

        var invocation = await AutomationSupport.ResolveInvocationAsync(
            config,
            isInputRedirected: true,
            stdin: new StringReader("ignored"),
            readFileAsync: _ => Task.FromResult("ignored"));

        Assert.True(invocation.IsNonInteractive);
        Assert.Equal(ProcessExitCode.InvalidArguments, invocation.ErrorExitCode);
        Assert.Contains("Specify only one", invocation.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveInvocationAsync_ReadsPromptFromStdin()
    {
        var config = new ProviderConfiguration
        {
            ReadPromptFromStdin = true
        };

        var invocation = await AutomationSupport.ResolveInvocationAsync(
            config,
            isInputRedirected: true,
            stdin: new StringReader("hello from stdin"),
            readFileAsync: _ => Task.FromResult(string.Empty));

        Assert.True(invocation.IsNonInteractive);
        Assert.Null(invocation.ErrorMessage);
        Assert.Equal("hello from stdin", invocation.Prompt);
    }

    [Fact]
    public void SerializeJson_IncludesExitCodeAndToolState()
    {
        var result = new NonInteractiveRunResult
        {
            FinalText = "done",
            MessageCount = 3,
            Provider = "openai",
            Model = "qwen-plus",
            BaseUrl = "https://dashscope-intl.aliyuncs.com/compatible-mode/v1"
        };
        result.AllowedTools.Add("read_file");
        result.ConfiguredDeniedTools.Add("bash");
        result.ExecutedTools.Add("read_file");
        result.ToolOutputs.Add(ToolExecutionRecord.Create("read_file", "{\"ok\":true}", isError: false));
        result.DeniedTools.Add("bash");

        var json = AutomationSupport.SerializeJson(result);
        using var document = JsonDocument.Parse(json);

        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal((int)ProcessExitCode.PermissionDenied, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("permission_denied", document.RootElement.GetProperty("terminationReason").GetString());
        Assert.Equal("done", document.RootElement.GetProperty("finalText").GetString());
        Assert.Equal("openai", document.RootElement.GetProperty("provider").GetString());
        Assert.Equal("qwen-plus", document.RootElement.GetProperty("model").GetString());
        Assert.Equal("read_file", document.RootElement.GetProperty("allowedTools")[0].GetString());
        Assert.Equal("bash", document.RootElement.GetProperty("configuredDeniedTools")[0].GetString());
        Assert.Equal("bash", document.RootElement.GetProperty("deniedTools")[0].GetString());
        Assert.Equal("read_file", document.RootElement.GetProperty("executedTools")[0].GetString());
        Assert.True(document.RootElement.GetProperty("toolOutputs")[0].GetProperty("isJson").GetBoolean());
        Assert.Equal("read_file", document.RootElement.GetProperty("toolOutputs")[0].GetProperty("toolName").GetString());
        Assert.True(document.RootElement.GetProperty("permissionPolicy").GetProperty("deniedTools")[0].ValueEquals("bash"));
    }

    [Fact]
    public void SerializeJson_DoesNotEscapeKoreanText()
    {
        var result = new NonInteractiveRunResult
        {
            FinalText = "안녕하세요",
            MessageCount = 2
        };

        var json = AutomationSupport.SerializeJson(result);

        Assert.Contains("안녕하세요", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\uC548", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolExecutionRecord_PreservesRawTextWhenPayloadIsNotJson()
    {
        var record = ToolExecutionRecord.Create("bash", "plain text", isError: true);

        Assert.Equal("bash", record.ToolName);
        Assert.True(record.IsError);
        Assert.False(record.IsJson);
        Assert.Equal("plain text", record.Raw);
        Assert.Null(record.Parsed);
    }

    [Fact]
    public void NonInteractiveRunResult_UsesCompletedTerminationReasonForSuccess()
    {
        var result = new NonInteractiveRunResult
        {
            FinalText = "ok",
            MessageCount = 2
        };

        Assert.Equal(ProcessExitCode.Success, result.ExitCode);
        Assert.Equal("completed", result.TerminationReason);
    }

    [Fact]
    public async Task CliNonInteractiveRunner_ReportsToolFailureWhenToolLoopHitsMaxSteps()
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
                        ToolName = "loop_tool",
                        PartialInput = "{}",
                        IsComplete = true
                    }
                },
                new StreamChunk { IsComplete = true });
        });
        var config = new ProviderConfiguration
        {
            Model = "test-model",
            Prompt = "loop"
        };
        config.AllowedToolNames.Add("loop_tool");

        var registry = new ToolRegistry();
        registry.Register(new FakeTool("loop_tool", ToolResult.Success("{\"status\":\"again\"}")));

        var result = await new CliNonInteractiveRunner(new CapturingAutomationOutput()).RunAsync(
            provider,
            config,
            new ChatConversationHistory(),
            registry,
            new NonInteractivePermissionEnforcer(allowedToolNames: config.AllowedToolNames),
            new CliToolLoopRunner(),
            "loop");

        Assert.Equal(45, providerCallCount);
        Assert.Equal(ProcessExitCode.ToolFailure, result.ExitCode);
        Assert.Equal("tool_error", result.TerminationReason);
        Assert.Equal("Stopped after reaching the maximum tool steps (45).", result.FinalText);
        Assert.Contains("Stopped after reaching the maximum tool steps (45).", result.ToolErrors);
    }

    [Fact]
    public async Task CliNonInteractiveRunner_ReportsToolFailureWhenBashExitCodeIsNonZero()
    {
        var provider = new ScriptedProvider(context =>
        {
            var sawToolResult = context.Messages
                .SelectMany(message => message.Content)
                .Any(content => content.Type == ContentType.ToolResult);

            return sawToolResult
                ? StreamSequence(
                    new StreamChunk { TextDelta = "verification failed" },
                    new StreamChunk { IsComplete = true })
                : StreamSequence(
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_bash",
                            ToolName = "bash",
                            PartialInput = "{\"command\":\"dotnet test\"}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk { IsComplete = true });
        });
        var config = new ProviderConfiguration
        {
            Model = "test-model",
            Prompt = "run tests"
        };
        config.AllowedToolNames.Add("bash");
        var registry = new ToolRegistry();
        registry.Register(new FakeTool(
            "bash",
            ToolResult.Success("""{"exitCode":1,"stdout":"1 failed","stderr":"","timeoutMs":30000}""")));

        var result = await new CliNonInteractiveRunner(new CapturingAutomationOutput()).RunAsync(
            provider,
            config,
            new ChatConversationHistory(),
            registry,
            new NonInteractivePermissionEnforcer(allowedToolNames: config.AllowedToolNames),
            new CliToolLoopRunner(),
            "run tests");

        Assert.Equal(ProcessExitCode.ToolFailure, result.ExitCode);
        Assert.Equal("tool_error", result.TerminationReason);
        Assert.Contains(result.ToolOutputs, record => record.ToolName == "bash" && record.IsError);
        Assert.Contains(result.ToolErrors, error => error.Contains("bash exited with code 1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CliNonInteractiveRunner_RetriesManualFallbackWithAllowedEditTool()
    {
        var providerCallCount = 0;
        var provider = new ScriptedProvider(context =>
        {
            providerCallCount++;
            if (context.Messages.SelectMany(message => message.Content).Any(content => content.Type == ContentType.ToolResult))
            {
                return StreamSequence(
                    new StreamChunk { TextDelta = "fixed" },
                    new StreamChunk { IsComplete = true });
            }

            return providerCallCount == 1
                ? StreamSequence(
                    new StreamChunk { TextDelta = "권한이 없어 직접 수정할 수 없습니다. 아래 코드를 복사해서 붙여넣으세요." },
                    new StreamChunk { IsComplete = true })
                : StreamSequence(
                    new StreamChunk
                    {
                        ToolUseDelta = new ToolUseChunk
                        {
                            ToolId = "tool_edit",
                            ToolName = "edit_file",
                            PartialInput = "{\"path\":\"Assets/Scripts/UI/ClickHandler.cs\"}",
                            IsComplete = true
                        }
                    },
                    new StreamChunk { IsComplete = true });
        });
        var config = new ProviderConfiguration
        {
            Model = "test-model",
            Prompt = "fix code"
        };
        config.AllowedToolNames.Add("edit_file");

        var registry = new ToolRegistry();
        registry.Register(new FakeTool("edit_file", ToolResult.Success("{\"status\":\"success\"}")));

        var result = await new CliNonInteractiveRunner(new CapturingAutomationOutput()).RunAsync(
            provider,
            config,
            new ChatConversationHistory(),
            registry,
            new NonInteractivePermissionEnforcer(allowedToolNames: config.AllowedToolNames),
            new CliToolLoopRunner(),
            "fix code");

        Assert.Equal(3, providerCallCount);
        Assert.Contains("edit_file", result.ExecutedTools);
        Assert.Contains(result.ToolOutputs, record => record.ToolName == "edit_file" && !record.IsError);
    }

    [Fact]
    public async Task CliNonInteractiveRunner_IncludesToolCapabilitySnapshotInPrompt()
    {
        ChatContext? capturedContext = null;
        var provider = new ScriptedProvider(context =>
        {
            capturedContext = context;
            return StreamSequence(
                new StreamChunk { TextDelta = "ok" },
                new StreamChunk { IsComplete = true });
        });
        var config = new ProviderConfiguration
        {
            Model = "test-model",
            Prompt = "fix code"
        };
        config.AllowedToolNames.Add("read_file");
        config.AllowedToolNames.Add("edit_file");
        config.DeniedToolNames.Add("bash");

        var registry = new ToolRegistry();
        registry.Register(new FakeTool("read_file", ToolResult.Success("{\"content\":\"ok\"}")));
        registry.Register(new FakeTool("edit_file", ToolResult.Success("{\"status\":\"success\"}")));
        registry.Register(new FakeTool("bash", ToolResult.Success("{\"exitCode\":0}")));
        registry.Register(new FakeTool("semantic_search", ToolResult.Success("{\"results\":[]}")));
        registry.TryRegister(new FakeTool("semantic_search", ToolResult.Success("{\"results\":\"duplicate\"}")));

        await new CliNonInteractiveRunner(new CapturingAutomationOutput()).RunAsync(
            provider,
            config,
            new ChatConversationHistory(),
            registry,
            new NonInteractivePermissionEnforcer(allowedToolNames: config.AllowedToolNames, deniedToolNames: config.DeniedToolNames),
            new CliToolLoopRunner(),
            "fix code");

        Assert.NotNull(capturedContext);
        Assert.Contains("Tool Permission State:", capturedContext!.SystemPrompt);
        Assert.Contains("allowed tools: edit_file(permission-gated), read_file(permission-gated)", capturedContext.SystemPrompt);
        Assert.Contains("denied tools: bash(permission-gated)", capturedContext.SystemPrompt);
        Assert.Contains("not allowed in this run: semantic_search(permission-gated)", capturedContext.SystemPrompt);
        Assert.Contains("skipped duplicate tool registrations: semantic_search", capturedContext.SystemPrompt);
        Assert.Contains("never say you lack permission for an allowed tool", capturedContext.SystemPrompt);
    }

    private static async IAsyncEnumerable<StreamChunk> StreamSequence(params StreamChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.Yield();
        }
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

    private sealed class CapturingAutomationOutput : ICliAutomationOutput
    {
        public void WriteResult(ProviderConfiguration config, NonInteractiveRunResult result)
        {
        }

        public void WriteError(ProviderConfiguration config, string message, ProcessExitCode exitCode)
        {
        }
    }

    private sealed class FakeTool(string name, ToolResult result) : ITool
    {
        public string Name => name;

        public string Description => $"{name} description";

        public object InputSchema => new { type = "object", properties = new { } };

        public bool RequiresPermission => true;

        public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default) =>
            Task.FromResult(result);
    }
}
