namespace AgentQ.Desktop.Services;

public static class DesktopTaskClassifier
{
    public static DesktopTaskKind Classify(string userText)
    {
        var text = userText.ToLowerInvariant();

        if (IsCodeReviewRequest(text))
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

        var asksToCreate = ContainsAny(
            text,
            "add",
            "implement",
            "create",
            "make",
            "build",
            "write",
            "\uB9CC\uB4E4",
            "\uCD94\uAC00",
            "\uAD6C\uD604",
            "\uC0DD\uC131",
            "\uC9DC",
            "\uC9C4\uD589",
            "\uC791\uC131",
            "\uC4F0",
            "\uB9CC\uC838");

        if (!asksToCreate &&
            ContainsAny(text, "analyze", "search", "\uBD84\uC11D", "\uAC80\uC0C9", "\uD30C\uC545", "\uCC3E\uC544", "\uC5B4\uB514", "\uD655\uC778", "\uC870\uC0AC"))
        {
            return DesktopTaskKind.Analysis;
        }

        if (ContainsAny(
                text,
                "add",
                "implement",
                "create",
                "write",
                "portfolio",
                "website",
                "web site",
                "homepage",
                "landing page",
                "python",
                "data analysis",
                "data tool",
                "\uD3EC\uD2B8\uD3F4\uB9AC\uC624",
                "\uC6F9\uC0AC\uC774\uD2B8",
                "\uC6F9 \uC0AC\uC774\uD2B8",
                "\uB2E8\uC5B4\uC7A5",
                "\uD648\uD398\uC774\uC9C0",
                "\uB79C\uB529",
                "\uC0AC\uC774\uD2B8",
                "\uD30C\uC774\uC36C",
                "\uB370\uC774\uD130 \uBD84\uC11D",
                "\uBD84\uC11D \uB3C4\uAD6C",
                "\uB9CC\uB4E4",
                "\uCD94\uAC00",
                "\uAD6C\uD604",
                "\uC9DC",
                "\uC791\uC131",
                "\uC4F0",
                "\uC9C4\uD589"))
        {
            return DesktopTaskKind.Feature;
        }

        return DesktopTaskKind.General;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static bool IsCodeReviewRequest(string text)
    {
        if (ContainsAny(text, "code review", "review code", "pr review", "pull request review", "diff review",
                "\uCF54\uB4DC \uB9AC\uBDF0", "\uCF54\uB4DC\uB9AC\uBDF0", "\uBCC0\uACBD\uC0AC\uD56D \uB9AC\uBDF0", "\uB514\uD504 \uB9AC\uBDF0"))
        {
            return true;
        }

        var hasReviewVerb = ContainsAny(text, "review", "\uB9AC\uBDF0", "\uAC80\uD1A0");
        var hasCodeTarget = ContainsAny(text,
            "code", "diff", "change", "changes", "commit", "pull request", " pr ", ".cs", ".js", ".ts", ".tsx", ".jsx",
            "\uCF54\uB4DC", "\uB514\uD504", "\uBCC0\uACBD\uC0AC\uD56D", "\uCEE4\uBC0B", "\uD480\uB9AC\uD018\uC2A4\uD2B8", "\uD30C\uC77C");

        return hasReviewVerb && hasCodeTarget;
    }
}

public enum TaskComplexity
{
    Simple,
    Moderate,
    Complex
}

public static class DesktopTaskComplexityEstimator
{
    public static TaskComplexity EstimateComplexity(string userText)
    {
        var text = userText.ToLowerInvariant();
        
        // Complex signals: references to multiple features, files, or multi-step requests
        var isComplex = text.Contains("and", StringComparison.OrdinalIgnoreCase) && 
                        (text.Contains("then", StringComparison.OrdinalIgnoreCase) || 
                         text.Contains("finally", StringComparison.OrdinalIgnoreCase)) ||
                        text.Contains("multiple", StringComparison.OrdinalIgnoreCase) ||
                        text.Contains("refactor everything", StringComparison.OrdinalIgnoreCase) ||
                        text.Contains("oauth", StringComparison.OrdinalIgnoreCase) ||
                        text.Contains("database", StringComparison.OrdinalIgnoreCase);

        if (isComplex)
        {
            return TaskComplexity.Complex;
        }

        var isModerate = text.Contains("add", StringComparison.OrdinalIgnoreCase) || 
                         text.Contains("implement", StringComparison.OrdinalIgnoreCase) || 
                         text.Contains("refactor", StringComparison.OrdinalIgnoreCase) || 
                         text.Contains("fix", StringComparison.OrdinalIgnoreCase);

        if (isModerate)
        {
            return TaskComplexity.Moderate;
        }

        return TaskComplexity.Simple;
    }
}
