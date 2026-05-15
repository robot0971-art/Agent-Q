using AgentQ.Core.Models;

namespace AgentQ.Desktop.Services;

public sealed class DesktopUsageTracker
{
    private int _requestCount;
    private int _totalInputTokens;
    private int _totalOutputTokens;
    private bool _hasActualForCurrentRequest;

    public DesktopUsageSnapshot RecordEstimate(string prompt, string output)
    {
        if (_hasActualForCurrentRequest)
        {
            _hasActualForCurrentRequest = false;
            return CreateSnapshot(0, 0, isEstimate: false, incrementRequest: false);
        }

        return Record(
            EstimateTokens(prompt),
            EstimateTokens(output),
            isEstimate: true);
    }

    public DesktopUsageSnapshot RecordActual(UsageStats usage)
    {
        _hasActualForCurrentRequest = true;
        return Record(
            usage.InputTokens,
            usage.OutputTokens,
            isEstimate: false);
    }

    private DesktopUsageSnapshot Record(int inputTokens, int outputTokens, bool isEstimate)
    {
        return CreateSnapshot(inputTokens, outputTokens, isEstimate, incrementRequest: true);
    }

    private DesktopUsageSnapshot CreateSnapshot(int inputTokens, int outputTokens, bool isEstimate, bool incrementRequest)
    {
        if (incrementRequest)
        {
            _requestCount++;
            _totalInputTokens += Math.Max(0, inputTokens);
            _totalOutputTokens += Math.Max(0, outputTokens);
        }

        return new DesktopUsageSnapshot
        {
            RequestCount = _requestCount,
            LastInputTokens = Math.Max(0, inputTokens),
            LastOutputTokens = Math.Max(0, outputTokens),
            TotalInputTokens = _totalInputTokens,
            TotalOutputTokens = _totalOutputTokens,
            IsEstimate = isEstimate
        };
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }
}
