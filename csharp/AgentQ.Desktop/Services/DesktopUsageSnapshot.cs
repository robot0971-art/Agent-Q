namespace AgentQ.Desktop.Services;

public sealed class DesktopUsageSnapshot
{
    public int RequestCount { get; init; }

    public int LastInputTokens { get; init; }

    public int LastOutputTokens { get; init; }

    public int LastTotalTokens => LastInputTokens + LastOutputTokens;

    public int TotalInputTokens { get; init; }

    public int TotalOutputTokens { get; init; }

    public int TotalTokens => TotalInputTokens + TotalOutputTokens;

    public bool IsEstimate { get; init; } = true;

    public string DisplayText
    {
        get
        {
            var suffix = IsEstimate ? " 추정" : string.Empty;
            return $"사용량: 마지막 {LastTotalTokens:n0}{suffix} / 누적 {TotalTokens:n0}{suffix} ({RequestCount:n0}회)";
        }
    }
}
