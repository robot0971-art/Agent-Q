namespace AgentQ.Desktop.Services;

public sealed class DesktopExecutionStrategy
{
    public required DesktopTaskKind Kind { get; init; }

    public required string Label { get; init; }

    public required IReadOnlyList<string> Steps { get; init; }

    public string FormatForPrompt()
    {
        var lines = Steps
            .Select((step, index) => $"{index + 1}. {step}")
            .ToArray();

        return $"Execution strategy ({Label}):{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }
}

public static class DesktopExecutionStrategyCatalog
{
    public static DesktopExecutionStrategy ForProfile(DesktopTaskProfile profile)
    {
        return profile.Kind switch
        {
            DesktopTaskKind.BugFix => new DesktopExecutionStrategy
            {
                Kind = profile.Kind,
                Label = "bug fix",
                Steps =
                [
                    "Gather failure evidence and identify the smallest relevant surface.",
                    "Reproduce or inspect the failure before editing when feasible.",
                    "Patch the minimal root cause without unrelated refactors.",
                    "Run focused verification for the changed path.",
                    "Summarize changed files, verification result, and residual risk."
                ]
            },
            DesktopTaskKind.Feature => new DesktopExecutionStrategy
            {
                Kind = profile.Kind,
                Label = "feature",
                Steps =
                [
                    "Inspect the existing pattern and public contracts first.",
                    "Draft a small implementation plan before broad edits.",
                    "Implement one cohesive change at a time.",
                    "Run the most relevant build or test command.",
                    "Summarize usage, changed files, and follow-up work."
                ]
            },
            DesktopTaskKind.VerificationFailure => new DesktopExecutionStrategy
            {
                Kind = profile.Kind,
                Label = "verification failure",
                Steps =
                [
                    "Classify the failure using compiler, linter, or test output.",
                    "Fix one failure class at a time.",
                    "Rerun the narrowest useful verification command.",
                    "Escalate to broader checks only after the narrow check passes.",
                    "Report the result and any remaining failure evidence."
                ]
            },
            DesktopTaskKind.Analysis => new DesktopExecutionStrategy
            {
                Kind = profile.Kind,
                Label = "analysis",
                Steps =
                [
                    "Gather a workspace snapshot and project-map evidence.",
                    "Search relevant symbols, files, and docs before making claims.",
                    "Separate confirmed facts from assumptions.",
                    "Recommend the next action with evidence and open questions."
                ]
            },
            DesktopTaskKind.CodeReview => new DesktopExecutionStrategy
            {
                Kind = profile.Kind,
                Label = "code review",
                Steps =
                [
                    "Inspect the diff or changed files first.",
                    "Prioritize bugs, regressions, security risks, and missing tests.",
                    "Cite concrete file or line evidence where available.",
                    "Keep summary secondary to findings."
                ]
            },
            DesktopTaskKind.Documentation => new DesktopExecutionStrategy
            {
                Kind = profile.Kind,
                Label = "documentation",
                Steps =
                [
                    "Inspect source, commands, or release state before writing claims.",
                    "Update only the user-facing docs needed for the request.",
                    "Keep unsupported behavior under needs-verification wording.",
                    "Summarize changed documentation and evidence used."
                ]
            },
            DesktopTaskKind.Refactor => new DesktopExecutionStrategy
            {
                Kind = profile.Kind,
                Label = "refactor",
                Steps =
                [
                    "Map call sites and dependency relationships first.",
                    "Preserve behavior and keep edits incremental.",
                    "Avoid broad cleanup outside the refactor target.",
                    "Run regression checks relevant to the touched surface.",
                    "Summarize compatibility risks and verification."
                ]
            },
            _ => new DesktopExecutionStrategy
            {
                Kind = DesktopTaskKind.General,
                Label = "general",
                Steps =
                [
                    "Clarify only when the missing detail blocks safe progress.",
                    "Gather the smallest useful context.",
                    "Take the safest useful action.",
                    "Summarize outcome and any remaining uncertainty."
                ]
            }
        };
    }
}
