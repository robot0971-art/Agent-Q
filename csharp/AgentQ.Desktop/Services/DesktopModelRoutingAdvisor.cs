using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public static class DesktopModelRoutingAdvisor
{
    private static readonly string[] ComplexSignals =
    [
        "architecture",
        "multi-agent",
        "refactor",
        "\uC124\uACC4",
        "\uAD6C\uC870",
        "\uC804\uCCB4",
        "\uBCF5\uC7A1",
        "\uB300\uADDC\uBAA8"
    ];

    private static readonly string[] SimpleSignals =
    [
        "typo",
        "\uBB38\uAD6C",
        "readme",
        "docs",
        "\uC124\uBA85",
        "\uCC3E\uC544",
        "\uD655\uC778",
        "\uBD84\uC11D"
    ];

    public static DesktopModelRoutingRecommendation Recommend(
        string userText,
        DesktopTaskProfile profile,
        ProviderConfiguration config,
        AgentWorkMode workMode)
    {
        var tier = ResolveTier(userText, profile, workMode);
        var suggestedModel = ChooseModel(config.Provider, tier);
        return new DesktopModelRoutingRecommendation
        {
            Tier = tier,
            Label = ToLabel(tier),
            SuggestedModel = suggestedModel,
            CurrentModelMatches = ModelMatches(config.Model, suggestedModel, tier),
            Reason = BuildReason(userText, profile, workMode, tier)
        };
    }

    private static DesktopModelRoutingTier ResolveTier(
        string userText,
        DesktopTaskProfile profile,
        AgentWorkMode workMode)
    {
        var text = userText.ToLowerInvariant();
        if (workMode == AgentWorkMode.Readonly ||
            (profile.Kind is DesktopTaskKind.Analysis or DesktopTaskKind.Documentation or DesktopTaskKind.CodeReview &&
             SimpleSignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase))))
        {
            return DesktopModelRoutingTier.SmallFast;
        }

        if (profile.Kind is DesktopTaskKind.Refactor or DesktopTaskKind.VerificationFailure ||
            ComplexSignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)) ||
            text.Length > 900)
        {
            return DesktopModelRoutingTier.LargeFrontier;
        }

        if (profile.Kind is DesktopTaskKind.Feature or DesktopTaskKind.BugFix)
        {
            return DesktopModelRoutingTier.Balanced;
        }

        return DesktopModelRoutingTier.Balanced;
    }

    private static string ChooseModel(string provider, DesktopModelRoutingTier tier)
    {
        var models = DesktopProviderModelCatalog.GetModels(provider);
        return tier switch
        {
            DesktopModelRoutingTier.SmallFast => models.FirstOrDefault(IsSmallFastModel) ?? models.LastOrDefault() ?? string.Empty,
            DesktopModelRoutingTier.LargeFrontier => models.FirstOrDefault(IsLargeFrontierModel) ?? models.FirstOrDefault() ?? string.Empty,
            _ => models.FirstOrDefault(IsBalancedModel) ?? models.FirstOrDefault() ?? string.Empty
        };
    }

    private static bool ModelMatches(string currentModel, string suggestedModel, DesktopModelRoutingTier tier)
    {
        if (string.IsNullOrWhiteSpace(currentModel) || string.IsNullOrWhiteSpace(suggestedModel))
        {
            return false;
        }

        if (string.Equals(currentModel, suggestedModel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return tier switch
        {
            DesktopModelRoutingTier.SmallFast => IsSmallFastModel(currentModel),
            DesktopModelRoutingTier.LargeFrontier => IsLargeFrontierModel(currentModel),
            _ => IsBalancedModel(currentModel)
        };
    }

    private static bool IsSmallFastModel(string model)
    {
        return ContainsAny(model, "mini", "nano", "flash", "lite", "haiku");
    }

    private static bool IsBalancedModel(string model)
    {
        return !IsSmallFastModel(model) && !ContainsAny(model, "opus", "pro");
    }

    private static bool IsLargeFrontierModel(string model)
    {
        return ContainsAny(model, "opus", "pro", "gpt-5.5", "gpt-5.4", "sonnet");
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToLabel(DesktopModelRoutingTier tier)
    {
        return tier switch
        {
            DesktopModelRoutingTier.SmallFast => "small-fast",
            DesktopModelRoutingTier.LargeFrontier => "large-frontier",
            _ => "balanced"
        };
    }

    private static string BuildReason(
        string userText,
        DesktopTaskProfile profile,
        AgentWorkMode workMode,
        DesktopModelRoutingTier tier)
    {
        return tier switch
        {
            DesktopModelRoutingTier.SmallFast => workMode == AgentWorkMode.Readonly
                ? $"Readonly {profile.Label} task can usually use a faster, cheaper model."
                : $"{profile.Label} task looks lightweight or mostly read-only.",
            DesktopModelRoutingTier.LargeFrontier => $"Task looks complex for {profile.Label}; prefer stronger reasoning before broad edits.",
            _ => $"{profile.Label} task is suitable for the current balanced coding route."
        };
    }
}
