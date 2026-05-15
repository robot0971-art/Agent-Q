namespace AgentQ.Desktop.Services;

public sealed class AgentVerificationPlan
{
    public required string Title { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string? Command { get; init; }

    public bool AlreadySatisfied { get; init; }

    public bool IsRunnable => !AlreadySatisfied && !string.IsNullOrWhiteSpace(Command);

    public string Detail => string.IsNullOrWhiteSpace(Command)
        ? Reason
        : $"{Command} - {Reason}";
}
