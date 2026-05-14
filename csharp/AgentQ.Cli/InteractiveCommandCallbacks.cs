namespace AgentQ.Cli;

public sealed class InteractiveCommandCallbacks
{
    public required Action Clear { get; init; }

    public required Action Help { get; init; }

    public required Func<Task> Setup { get; init; }

    public required Action History { get; init; }

    public required Func<Task> Compact { get; init; }

    public required Action Tools { get; init; }

    public required Action<string> Permissions { get; init; }

    public required Action Status { get; init; }

    public required Func<string, Task> RunTool { get; init; }

    public required Func<string, Task> Provider { get; init; }

    public required Action<string> Model { get; init; }

    public required Action<string> BaseUrl { get; init; }

    public required Action<string> ApiKey { get; init; }

    public required Action<string> Timeout { get; init; }

    public required Action<string> MaxTokens { get; init; }

    public required Func<string, Task> Config { get; init; }

    public required Func<string, Task> Save { get; init; }

    public required Func<string, Task> Load { get; init; }

    public required Action<string> Unknown { get; init; }
}
