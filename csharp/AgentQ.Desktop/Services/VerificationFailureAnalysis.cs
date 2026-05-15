namespace AgentQ.Desktop.Services;

public sealed class VerificationFailureAnalysis
{
    public VerificationFailureKind Kind { get; init; } = VerificationFailureKind.Unknown;

    public string Title { get; init; } = "Unknown verification failure";

    public string Summary { get; init; } = "The verification command failed, but AgentQ could not classify the cause.";

    public string SuggestedNextStep { get; init; } = "Inspect the verification output and relevant files before editing.";

    public IReadOnlyList<string> Evidence { get; init; } = [];

    public string DisplayText
    {
        get
        {
            var evidence = Evidence.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}Evidence:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", Evidence)}";

            return $"{Title}{Environment.NewLine}{Summary}{Environment.NewLine}Next: {SuggestedNextStep}{evidence}";
        }
    }
}
