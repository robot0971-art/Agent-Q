namespace AgentQ.Desktop.Services;

public static class DesktopTaskClassifier
{
    public static DesktopTaskKind Classify(string userText)
    {
        var text = userText.ToLowerInvariant();

        if (ContainsAny(text, "review", "code review", "\uB9AC\uBDF0", "\uAC80\uD1A0"))
        {
            return DesktopTaskKind.CodeReview;
        }

        if (ContainsAny(text, "readme", "docs", "document", "\uBB38\uC11C", "\uB9B4\uB9AC\uC988 \uB178\uD2B8", "\uC124\uBA85"))
        {
            return DesktopTaskKind.Documentation;
        }

        if (ContainsAny(text, "verify", "\uAC80\uC99D", "\uD14C\uC2A4\uD2B8", "\uBE4C\uB4DC \uC2E4\uD328", "\uCEF4\uD30C\uC77C"))
        {
            return DesktopTaskKind.VerificationFailure;
        }

        if (ContainsAny(text, "test failed", "build failed", "verification failed", "\uC2E4\uD328", "\uC5D0\uB7EC", "\uC624\uB958", "\uC548\uB428", "\uC548 \uB3FC", "\uACE0\uCCD0", "\uACE0\uCE58", "fix", "bug", "\uBC84\uADF8"))
        {
            return DesktopTaskKind.BugFix;
        }

        if (ContainsAny(text, "refactor", "\uB9AC\uD329\uD130", "\uB9AC\uD329\uD1A0", "\uC815\uB9AC", "\uAD6C\uC870 \uAC1C\uC120"))
        {
            return DesktopTaskKind.Refactor;
        }

        if (ContainsAny(text, "analyze", "\uBD84\uC11D", "\uD30C\uC545", "\uCC3E\uC544", "\uC5B4\uB514", "\uD655\uC778", "\uC870\uC0AC"))
        {
            return DesktopTaskKind.Analysis;
        }

        if (ContainsAny(text, "add", "implement", "create", "\uB9CC\uB4E4", "\uCD94\uAC00", "\uAD6C\uD604", "\uC9DC", "\uC9C4\uD589"))
        {
            return DesktopTaskKind.Feature;
        }

        return DesktopTaskKind.General;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
