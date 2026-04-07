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
            MessageCount = 3
        };
        result.AllowedTools.Add("read_file");
        result.ToolOutputs.Add("{\"ok\":true}");
        result.DeniedTools.Add("bash");

        var json = AutomationSupport.SerializeJson(result);
        using var document = JsonDocument.Parse(json);

        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal((int)ProcessExitCode.PermissionDenied, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("permission_denied", document.RootElement.GetProperty("terminationReason").GetString());
        Assert.Equal("done", document.RootElement.GetProperty("finalText").GetString());
        Assert.Equal("read_file", document.RootElement.GetProperty("allowedTools")[0].GetString());
        Assert.Equal("bash", document.RootElement.GetProperty("deniedTools")[0].GetString());
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
