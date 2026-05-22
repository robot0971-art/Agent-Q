namespace AgentQ.Desktop.Services;

public sealed class DesktopModelRoutingRecommendation
{
    public DesktopModelRoutingTier Tier { get; init; } = DesktopModelRoutingTier.Balanced;

    public string Label { get; init; } = "balanced";

    public string Reason { get; init; } = string.Empty;

    public string SuggestedModel { get; init; } = string.Empty;

    public bool CurrentModelMatches { get; init; }

    public string DisplayText => string.IsNullOrWhiteSpace(SuggestedModel)
        ? $"{Label}: {Reason}"
        : $"{Label}: {SuggestedModel} - {Reason}";
}
