using System.Text.Json;
using AgentQ.Cli;
using AgentQ.Core.Providers;
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
}
