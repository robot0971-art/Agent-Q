namespace AgentQ.Desktop.Services;

public sealed class DesktopConfidenceAssessment
{
    public required int Score { get; init; }

    public required string Level { get; init; }

    public IReadOnlyList<string> Signals { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public string DisplayText
    {
        get
        {
            var parts = new List<string>
            {
                $"Score: {Score}%"
            };

            if (Signals.Count > 0)
            {
                parts.Add($"Signals: {string.Join("; ", Signals)}");
            }

            if (Warnings.Count > 0)
            {
                parts.Add($"Warnings: {string.Join("; ", Warnings)}");
            }

            return string.Join(Environment.NewLine, parts);
        }
    }
}
