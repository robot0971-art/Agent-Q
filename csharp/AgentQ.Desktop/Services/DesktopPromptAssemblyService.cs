using System.Text;

namespace AgentQ.Desktop.Services;

public static class DesktopPromptAssemblyService
{
    private static readonly IReadOnlyList<IDesktopPromptRule> PromptRules =
    [
        new ContextPrioritizationPromptRule(),
        new ToolRoutingPromptRule(),
        new UserIntentPrecedencePromptRule(),
        new ScaffoldDecisionPromptRule(),
        new ExecutionStrategyPromptRule(),
        new TaskTrackingPromptRule(),
        new MultiAgentRolePromptRule(),
        new LinkHandlingPromptRule(),
        new VerificationFailurePromptRule(),
        new EvidenceBackedPromptRule(),
        new FinalReportingPromptRule()
    ];

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
                SystemHint = "Task mode: documentation. Inspect source or release state before writing claims, keep wording clear, cite the inspected files as evidence, and avoid inventing unsupported behavior.",
                ContextHint = "Documentation context: prioritize README, docs, release notes, public commands, and user-facing behavior."
            },
            DesktopTaskKind.Analysis => new DesktopTaskProfile
            {
                Kind = kind,
                Label = "analysis",
                SystemHint = "Task mode: analysis. Prefer read-only tools, use hybrid_search and project map evidence, cite inspected files as evidence, separate confirmed facts from assumptions, and summarize findings before recommending changes.",
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

    public static string BuildSystemPrompt(string basePrompt, DesktopTaskProfile profile, string? toolPermissionState = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(basePrompt.TrimEnd());
        builder.AppendLine();
        builder.AppendLine("Dynamic task guidance:");
        builder.AppendLine(profile.SystemHint);
        if (!string.IsNullOrWhiteSpace(toolPermissionState))
        {
            builder.AppendLine();
            builder.AppendLine(toolPermissionState.Trim());
        }

        foreach (var rule in PromptRules.Where(rule => rule.Applies(profile)))
        {
            builder.AppendLine();
            builder.AppendLine(rule.Build(profile));
        }

        return builder.ToString().TrimEnd();
    }
}

public interface IDesktopPromptRule
{
    bool Applies(DesktopTaskProfile profile);

    string Build(DesktopTaskProfile profile);
}

public sealed class ContextPrioritizationPromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) => !string.IsNullOrWhiteSpace(profile.ContextHint);

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Context prioritization:");
        builder.AppendLine(profile.ContextHint);
        return builder.ToString().TrimEnd();
    }
}

public sealed class LinkHandlingPromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) => true;

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Link handling rules:");
        builder.AppendLine("- AgentQ Desktop can attempt to fetch HTTP/HTTPS URLs when link auto-read is enabled.");
        builder.AppendLine("- Never answer that AgentQ categorically cannot access external websites; explain the link auto-read setting, fetch result, or fallback instead.");
        builder.AppendLine("- If no URL is present, ask the user to send the URL. If a fetch fails, report the failure reason and suggest pasted text or a local file as fallback.");
        return builder.ToString().TrimEnd();
    }
}

public sealed class ToolRoutingPromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) => true;

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Tool routing rules:");
        builder.AppendLine("- Use project map, memory, and existing evidence first when they already identify the relevant files.");
        builder.AppendLine("- Use list_directory to inspect folder structure, confirm an empty workspace, or discover top-level files before using shell directory commands.");
        builder.AppendLine("- Use symbol_search for known class, method, function, component, or type names.");
        builder.AppendLine("- Use grep_search for exact strings, errors, UI labels, config keys, commands, and log fragments.");
        builder.AppendLine("- Use hybrid_search when the request is conceptual, cross-file, or the exact identifier is unknown.");
        builder.AppendLine("- Use semantic_search only when an embedding index is available and keyword/symbol search is likely too narrow.");
        builder.AppendLine("- Use MCP bridge tools only for project-configured external systems that native AgentQ tools cannot inspect directly.");
        builder.AppendLine("- Prefer read_file after search identifies a small candidate set; avoid broad file reads when a narrower search can locate the owner.");
        builder.AppendLine("- Use bash for build/test, Git, or commands that native read/search tools cannot cover; do not use bash just to list files.");
        return builder.ToString().TrimEnd();
    }
}

public sealed class UserIntentPrecedencePromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) => true;

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("User intent precedence:");
        builder.AppendLine("- The user's latest explicit instruction overrides earlier assistant recommendations, inferred defaults, project guesses, and scaffold preferences.");
        builder.AppendLine("- If you recommended TypeScript but the user asks for JavaScript, implement JavaScript files and commands. Do not continue with TypeScript unless the user changes back.");
        builder.AppendLine("- For greenfield portfolio/homepage/website work, default to JavaScript unless the user explicitly asks for TypeScript.");
        builder.AppendLine("- If the user corrects stack, framework, language, style, or scope, acknowledge internally and update the plan before touching files.");
        builder.AppendLine("- Use defaults only for details the user has not specified. Never replace a specified choice with a default because it seems more modern or safer.");
        builder.AppendLine("- When a user says 'not that', 'instead', 'use X', or similar correction, treat it as a hard constraint for the current task.");
        return builder.ToString().TrimEnd();
    }
}

public sealed class ScaffoldDecisionPromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) => profile.Kind is DesktopTaskKind.Feature;

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Scaffold decision rules:");
        builder.AppendLine("- Treat scaffold recommendations as optional accelerators, not as automatic instructions.");
        builder.AppendLine("- A bare request for a 'new project' does not contain enough product intent. Ask what kind of project the user wants before picking React, Python, API, game, or any other stack.");
        builder.AppendLine("- Desktop preflight may attach a deterministic project scaffold plan. Follow that plan unless the user changes project type, language, framework, or style.");
        builder.AppendLine("- Use plan_project_scaffold to re-plan after the user clarifies or changes greenfield project direction; it does not create files.");
        builder.AppendLine("- Use create_project_scaffold only with the planId and planHash returned by plan_project_scaffold or attached by preflight; do not create from a request string alone.");
        builder.AppendLine("- After create_project_scaffold succeeds, use verify_project_scaffold with the same approved planId and planHash to run only plan-listed verification commands.");
        builder.AppendLine("- If verify_project_scaffold returns succeeded=false, use its failureAnalysis and repairPrompt to inspect and repair the scaffold before rerunning verification.");
        builder.AppendLine("- First decide whether the user's latest request has enough product intent to act. Ask a focused question only when missing choices would materially change the result.");
        builder.AppendLine("- If the request names a concrete stack or artifact, create the smallest useful project/files with workspace tools even when no scaffold recommendation exactly matches.");
        builder.AppendLine("- If a scaffold recommendation matches the request and workspace state, you may mirror its file structure, but adapt stack, language, sections, and copy to the user's latest wording.");
        builder.AppendLine("- Do not reset to a greeting or broad 'what do you want to build?' question after the user has already named a project direction.");
        return builder.ToString().TrimEnd();
    }
}

public sealed class ExecutionStrategyPromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) => true;

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine(DesktopExecutionStrategyCatalog.ForProfile(profile).FormatForPrompt());
        builder.AppendLine("Follow these stages unless the user explicitly asks for a different workflow.");
        return builder.ToString().TrimEnd();
    }
}

public sealed class VerificationFailurePromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) => profile.Kind is DesktopTaskKind.VerificationFailure;

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Verification failure response rules:");
        builder.AppendLine("- Prefer fixing one failure class at a time.");
        builder.AppendLine("- Treat compiler, linter, and test output as primary evidence.");
        builder.AppendLine("- Rerun the narrowest useful verification command before broad checks.");
        return builder.ToString().TrimEnd();
    }
}

public sealed class TaskTrackingPromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) =>
        profile.Kind is DesktopTaskKind.Feature or DesktopTaskKind.BugFix or DesktopTaskKind.Refactor or DesktopTaskKind.VerificationFailure;

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Task tracking rules:");
        builder.AppendLine("- For work with 3+ distinct steps, maintain a concise checklist in the response or plan panel rather than keeping progress implicit.");
        builder.AppendLine("- Keep exactly one item in progress while work remains; mark items complete only after the required action and useful verification are actually done.");
        builder.AppendLine("- If requirements are ambiguous, ask a focused question and leave the task in a waiting-for-answer state instead of claiming completion.");
        builder.AppendLine("- Preserve user-provided commands, flags, file names, stack choices, and order exactly when turning them into plan items.");
        builder.AppendLine("- If blocked or partially complete, say what remains and the concrete next action; do not summarize partial work as finished.");
        return builder.ToString().TrimEnd();
    }
}

public sealed class MultiAgentRolePromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) => true;

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine(MultiAgentRolePlanner.Build(profile).FormatForPrompt());
        builder.AppendLine("Use this as a local role checklist in v1. Do not claim separate agents ran unless the product explicitly executes them.");
        return builder.ToString().TrimEnd();
    }
}

public sealed class EvidenceBackedPromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) =>
        profile.Kind is DesktopTaskKind.Analysis or DesktopTaskKind.Documentation or DesktopTaskKind.CodeReview;

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Evidence-backed response rules:");
        builder.AppendLine("- Ground technology stack, package, framework, and architecture claims in inspected files or tool output.");
        builder.AppendLine("- Include a short Evidence section with the most relevant file paths, commands, or search results used.");
        builder.AppendLine("- Include a short Needs verification section for anything inferred from naming, folder layout, memory, or incomplete search results.");
        builder.AppendLine("- Do not claim that a dependency, worker library, incremental index strategy, or release state exists unless a supporting file or command output was inspected.");
        builder.AppendLine("- For URL questions, report whether link auto-read is enabled, whether the fetch succeeded or failed, and what evidence was available.");
        return builder.ToString().TrimEnd();
    }
}

public sealed class FinalReportingPromptRule : IDesktopPromptRule
{
    public bool Applies(DesktopTaskProfile profile) => true;

    public string Build(DesktopTaskProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Final response rules:");
        builder.AppendLine("- When files were changed, summarize the root cause, changed files, and action taken in concise Korean.");
        builder.AppendLine("- Mention the exact verification command and result when verification ran successfully; if verification did not run, say what was not verified and why.");
        builder.AppendLine("- Do not include long code blocks or diff blocks unless the user explicitly asks for code.");
        builder.AppendLine("- Do not tell the user to copy and paste code when edit tools are available; use the tools, then report what changed.");
        builder.AppendLine("- Do not ask the user to manually verify a build or test that already passed during the run.");
        builder.AppendLine("- Keep edits inside the user's requested scope. If you discover additional unrelated bugs, report them as optional follow-up findings and ask before modifying them.");
        builder.AppendLine("- For compile or test-failure requests, fix the minimal root cause needed for that failure first; do not bundle opportunistic gameplay, UX, refactor, or cleanup fixes into the same run unless the user asked for them.");
        return builder.ToString().TrimEnd();
    }
}
