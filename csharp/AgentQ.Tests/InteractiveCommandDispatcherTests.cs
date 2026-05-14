using AgentQ.Cli;
using Xunit;

namespace AgentQ.Tests;

public sealed class InteractiveCommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_ReturnsFalseForExitCommands()
    {
        var dispatcher = new InteractiveCommandDispatcher(CreateCallbacks());

        Assert.False(await dispatcher.DispatchAsync("/exit"));
        Assert.False(await dispatcher.DispatchAsync("/quit"));
    }

    [Fact]
    public async Task DispatchAsync_RoutesCommandArguments()
    {
        var routed = new List<string>();
        var dispatcher = new InteractiveCommandDispatcher(CreateCallbacks(
            provider: argument =>
            {
                routed.Add($"provider:{argument}");
                return Task.CompletedTask;
            },
            maxTokens: argument => routed.Add($"max-tokens:{argument}"),
            config: argument =>
            {
                routed.Add($"config:{argument}");
                return Task.CompletedTask;
            }));

        Assert.True(await dispatcher.DispatchAsync("/provider opencode-go"));
        Assert.True(await dispatcher.DispatchAsync("/max-tokens 8192"));
        Assert.True(await dispatcher.DispatchAsync("/config save"));

        Assert.Equal(
            ["provider:opencode-go", "max-tokens:8192", "config:save"],
            routed);
    }

    [Fact]
    public async Task DispatchAsync_ReportsUnknownCommands()
    {
        var unknownCommands = new List<string>();
        var dispatcher = new InteractiveCommandDispatcher(CreateCallbacks(
            unknown: unknownCommands.Add));

        Assert.True(await dispatcher.DispatchAsync("/missing"));

        Assert.Equal(["/missing"], unknownCommands);
    }

    private static InteractiveCommandCallbacks CreateCallbacks(
        Action? clear = null,
        Action? help = null,
        Func<Task>? setup = null,
        Action? history = null,
        Func<Task>? compact = null,
        Action? tools = null,
        Action<string>? permissions = null,
        Action? status = null,
        Func<string, Task>? runTool = null,
        Func<string, Task>? provider = null,
        Action<string>? model = null,
        Action<string>? baseUrl = null,
        Action<string>? apiKey = null,
        Action<string>? timeout = null,
        Action<string>? maxTokens = null,
        Func<string, Task>? config = null,
        Func<string, Task>? save = null,
        Func<string, Task>? load = null,
        Action<string>? unknown = null)
    {
        return new InteractiveCommandCallbacks
        {
            Clear = clear ?? (() => { }),
            Help = help ?? (() => { }),
            Setup = setup ?? (() => Task.CompletedTask),
            History = history ?? (() => { }),
            Compact = compact ?? (() => Task.CompletedTask),
            Tools = tools ?? (() => { }),
            Permissions = permissions ?? (_ => { }),
            Status = status ?? (() => { }),
            RunTool = runTool ?? (_ => Task.CompletedTask),
            Provider = provider ?? (_ => Task.CompletedTask),
            Model = model ?? (_ => { }),
            BaseUrl = baseUrl ?? (_ => { }),
            ApiKey = apiKey ?? (_ => { }),
            Timeout = timeout ?? (_ => { }),
            MaxTokens = maxTokens ?? (_ => { }),
            Config = config ?? (_ => Task.CompletedTask),
            Save = save ?? (_ => Task.CompletedTask),
            Load = load ?? (_ => Task.CompletedTask),
            Unknown = unknown ?? (_ => { })
        };
    }
}
