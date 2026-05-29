namespace AgentQ.Desktop.Services;

public sealed class AutoFixLoopGuard
{
    public const int DefaultRepeatedFailureLimit = 3;

    public AutoFixLoopGuardDecision RecordFailure(
        AutoFixLoopGuardState state,
        string failureSignature,
        int repeatedFailureLimit = DefaultRepeatedFailureLimit)
    {
        if (string.IsNullOrWhiteSpace(failureSignature))
        {
            return new AutoFixLoopGuardDecision
            {
                State = AutoFixLoopGuardState.Empty,
                ShouldStop = false
            };
        }

        var count = string.Equals(state.FailureSignature, failureSignature, StringComparison.Ordinal)
            ? state.RepeatedCount + 1
            : 1;
        var nextState = new AutoFixLoopGuardState(failureSignature, count);

        return new AutoFixLoopGuardDecision
        {
            State = nextState,
            ShouldStop = count >= repeatedFailureLimit,
            Message = count >= repeatedFailureLimit
                ? $"The same verification failure repeated {count:0} time(s)."
                : string.Empty
        };
    }
}

public readonly record struct AutoFixLoopGuardState(string FailureSignature, int RepeatedCount)
{
    public static AutoFixLoopGuardState Empty { get; } = new(string.Empty, 0);
}

public sealed class AutoFixLoopGuardDecision
{
    public required AutoFixLoopGuardState State { get; init; }

    public bool ShouldStop { get; init; }

    public string Message { get; init; } = string.Empty;
}
