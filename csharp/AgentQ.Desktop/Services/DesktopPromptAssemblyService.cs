using System.Text;

namespace AgentQ.Desktop.Services;

public static class DesktopPromptAssemblyService
{
    public static DesktopTaskProfile BuildTaskProfile(string userText)
    {
        var kind = DesktopTaskClassifier.Classify(userText);
        return kind switch
        {
            DesktopTaskKind.BugFix => new DesktopTaskProfile
            {
                Kind = kind,
                Label = "bug-fix",
                SystemHint = "Task mode: bug fix. Reproduce or inspect the failure evidence first, use hybrid_search for likely files, keep the patch minimal, then run focused verification.",
                ContextHint = "Bug-fix context: prioritize failure evidence, recent edits, related symbols, and verification commands. Avoid broad refactors unless required."
            },
            DesktopTaskKind.Feature => new DesktopTaskProfile
            {
                Kind = kind,
                Label = "feature",
                SystemHint = "Task mode: feature implementation. Map the existing pattern first, identify the smallest coherent change, update tests or docs when useful, then verify.",
                ContextHint = "Feature context: prioritize project map, nearby implementations, public contracts, and expected verification commands."
            },
            DesktopTaskKind.CodeReview => new DesktopTaskProfile
            {
                Kind = kind,
                Label = "code-review",
                SystemHint = "Task mode: code review. Do not modify files unless asked. Lead with concrete risks, regressions, security issues, and missing tests.",
                ContextHint = "Review context: prioritize git diff, changed files, test gaps, and exact file or line references."
            },
            DesktopTaskKind.VerificationFailure => new DesktopTaskProfile
            {
                Kind = kind,
                Label = "verification-failure",
                SystemHint = "Task mode: verification failure. Treat compiler, linter, and test output as primary evidence; fix the root cause and rerun the narrowest useful check.",
                ContextHint = "Verification context: prioritize command output, failure classifier evidence, touched files, and known recurring errors."
            },
            DesktopTaskKind.Documentation => new DesktopTaskProfile
            {
                Kind = kind,
                Label = "documentation",
                SystemHint = "Task mode: documentation. Inspect source or release state before writing claims, keep wording clear, and avoid inventing unsupported behavior.",
                ContextHint = "Documentation context: prioritize README, docs, release notes, public commands, and user-facing behavior."
            },
            DesktopTaskKind.Analysis => new DesktopTaskProfile
            {
                Kind = kind,
                Label = "analysis",
                SystemHint = "Task mode: analysis. Prefer read-only tools, use hybrid_search and project map evidence, summarize findings before recommending changes.",
                ContextHint = "Analysis context: prioritize project map, key files, symbols, and evidence trail. Avoid edits unless the user clearly asks to proceed."
            },
            DesktopTaskKind.Refactor => new DesktopTaskProfile
            {
                Kind = kind,
                Label = "refactor",
                SystemHint = "Task mode: refactor. Preserve behavior, inspect call sites with symbol and keyword search, keep changes incremental, and run regression checks.",
                ContextHint = "Refactor context: prioritize call sites, dependency relationships, tests, and compatibility risks."
            },
            _ => new DesktopTaskProfile
            {
                Kind = DesktopTaskKind.General,
                Label = "general",
                SystemHint = "Task mode: general. Clarify only when necessary, otherwise inspect the workspace and choose the safest useful next action.",
                ContextHint = "General context: use only relevant workspace, memory, link, and search context for this request."
            }
        };
    }

    public static string BuildSystemPrompt(string basePrompt, DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine(basePrompt.TrimEnd());
        builder.AppendLine();
        builder.AppendLine("Dynamic task guidance:");
        builder.AppendLine(profile.SystemHint);
        return builder.ToString().TrimEnd();
    }
}
