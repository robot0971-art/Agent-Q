using System.Text;

namespace AgentQ.Desktop.Services;

public static class DesktopPromptBuilder
{
    public static string BuildVerificationFixPrompt(
        AgentVerificationPlan plan,
        VerificationRunResult? result,
        VerificationFailureAnalysis? analysis)
    {
        var output = result?.CombinedOutput ?? string.Empty;
        output = Truncate(output, 12000, "[verification output truncated]");

        var builder = new StringBuilder();
        builder.AppendLine("The last verification command failed. Analyze the failure, inspect the relevant files, fix the issue, and run an appropriate verification again.");
        builder.AppendLine("Use the failure classification as a starting hypothesis, but verify it against the output and source files.");
        builder.AppendLine();
        builder.AppendLine($"Command: {plan.Command}");
        builder.AppendLine($"Exit code: {(result == null ? "n/a" : result.ExitCode.ToString())}");

        if (analysis != null)
        {
            builder.AppendLine();
            builder.AppendLine("Failure classification:");
            builder.AppendLine($"Kind: {analysis.Kind}");
            builder.AppendLine($"Title: {analysis.Title}");
            builder.AppendLine($"Summary: {analysis.Summary}");
            builder.AppendLine($"Suggested next step: {analysis.SuggestedNextStep}");
            if (analysis.Evidence.Count > 0)
            {
                builder.AppendLine("Evidence:");
                foreach (var item in analysis.Evidence)
                {
                    builder.AppendLine($"- {item}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(plan.Reason))
        {
            builder.AppendLine($"Reason this verification was selected: {plan.Reason}");
        }

        builder.AppendLine();
        builder.AppendLine("Verification output:");
        builder.AppendLine(output);
        return builder.ToString().TrimEnd();
    }

    public static string BuildPlannerPrompt(string goal)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Create a durable execution plan for this work.");
        builder.AppendLine("Do not modify files yet. Break the goal into ordered milestones, checkpoints, verification points, and likely risks.");
        builder.AppendLine("For each milestone, include clear done criteria and the first concrete next action.");
        builder.AppendLine("Prefer a plan that can be resumed after interruption.");
        builder.AppendLine("Return the plan as a markdown checklist under a 'Plan' heading, using '- [ ]' for pending work.");
        builder.AppendLine();
        builder.AppendLine("Goal or recent context:");
        builder.AppendLine(goal);
        return builder.ToString().TrimEnd();
    }

    public static string BuildContinuePlanItemPrompt(AgentPlanItem item)
    {
        return BuildContinuePlanItemPrompt(item, []);
    }

    public static string BuildContinuePlanItemPrompt(
        AgentPlanItem item,
        IEnumerable<AgentPlanItem> planItems)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Continue the current multi-step plan by working on this plan item.");
        builder.AppendLine("Inspect the workspace as needed, make only the changes required for this item, and run appropriate verification.");
        builder.AppendLine("Keep the work scoped to the selected item. Do not jump ahead unless it is required to unblock this item.");
        builder.AppendLine("When done, summarize what changed, what verification passed, and whether this plan item is complete.");
        builder.AppendLine();
        var allItems = planItems.OrderBy(plan => plan.Order).ToList();
        if (allItems.Count > 0)
        {
            builder.AppendLine("Full plan:");
            foreach (var plan in allItems)
            {
                builder.AppendLine($"- [{GetPlanMark(plan.Status)}] {plan.Order}. {plan.Title}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("Plan item:");
        builder.AppendLine($"{item.Order}. {item.Title}");
        if (!string.IsNullOrWhiteSpace(item.Detail))
        {
            builder.AppendLine(item.Detail);
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildResumePrompt(AgentCheckpoint checkpoint)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Resume the previous AgentQ work from this checkpoint.");
        builder.AppendLine("First restate the last known state, then inspect the workspace as needed, choose the next concrete step, and continue.");
        builder.AppendLine("Respect current git changes. Do not discard user edits.");
        builder.AppendLine();
        builder.AppendLine("Checkpoint:");
        builder.AppendLine(BuildCheckpointDisplayText(checkpoint));

        if (checkpoint.Conversation.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Recent conversation:");
            foreach (var message in checkpoint.Conversation.TakeLast(12))
            {
                builder.AppendLine($"{message.Role}:");
                builder.AppendLine(Truncate(message.Content, 3000));
                builder.AppendLine();
            }
        }

        if (checkpoint.Logs.Count > 0)
        {
            builder.AppendLine("Recent logs:");
            foreach (var log in checkpoint.Logs.TakeLast(20))
            {
                builder.AppendLine($"- {log}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildResumeFromSessionSummaryPrompt(AgentSessionSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Resume the previous AgentQ work from this saved session summary.");
        builder.AppendLine("First restate the last known state, inspect the workspace as needed, then continue with the most useful next step.");
        builder.AppendLine("Respect current git changes. Do not discard user edits.");
        builder.AppendLine();
        builder.AppendLine(summary.DisplayText);
        return builder.ToString().TrimEnd();
    }

    public static string BuildCodeReviewPrompt(
        GitCommandResult status,
        GitCommandResult diffStat,
        GitCommandResult fullDiff)
    {
        var diff = Truncate(fullDiff.DisplayOutput, 24000, "[diff truncated; inspect files directly if needed]");

        var builder = new StringBuilder();
        builder.AppendLine("Review the current workspace changes like a senior code reviewer.");
        builder.AppendLine("Do not modify files yet. Prioritize bugs, regressions, security risks, unsafe behavior, and missing tests.");
        builder.AppendLine("Lead with findings ordered by severity, include file/line references when possible, then list open questions and residual test risk.");
        builder.AppendLine("If the diff is incomplete or untracked files are present, inspect the relevant files before concluding.");
        builder.AppendLine();
        builder.AppendLine("Git status:");
        builder.AppendLine(status.DisplayOutput);
        builder.AppendLine();
        builder.AppendLine("Git diff stat:");
        builder.AppendLine(diffStat.DisplayOutput);
        builder.AppendLine();
        builder.AppendLine("Git diff:");
        builder.AppendLine(diff);
        return builder.ToString().TrimEnd();
    }

    public static string BuildCodeReviewFixPrompt(
        string review,
        GitCommandResult status,
        GitCommandResult diffStat,
        GitCommandResult fullDiff)
    {
        var reviewText = Truncate(review, 12000, "[review truncated]");
        var diff = Truncate(fullDiff.DisplayOutput, 18000, "[diff truncated; inspect files directly if needed]");

        var builder = new StringBuilder();
        builder.AppendLine("Fix the actionable findings from the last code review.");
        builder.AppendLine("Inspect the relevant files before editing. Keep changes tightly scoped to the review findings.");
        builder.AppendLine("After editing, run the appropriate verification commands and report what passed.");
        builder.AppendLine("If a review item is not actionable or is already false, explain why and leave it unchanged.");
        builder.AppendLine();
        builder.AppendLine("Last code review:");
        builder.AppendLine(reviewText);
        builder.AppendLine();
        builder.AppendLine("Current git status:");
        builder.AppendLine(status.DisplayOutput);
        builder.AppendLine();
        builder.AppendLine("Current git diff stat:");
        builder.AppendLine(diffStat.DisplayOutput);
        builder.AppendLine();
        builder.AppendLine("Current git diff:");
        builder.AppendLine(diff);
        return builder.ToString().TrimEnd();
    }

    public static string BuildCommitSummaryPrompt(
        GitCommandResult status,
        GitCommandResult diffStat,
        GitCommandResult fullDiff)
    {
        var diff = Truncate(fullDiff.DisplayOutput, 24000, "[diff truncated; inspect files directly if needed]");

        var builder = new StringBuilder();
        builder.AppendLine("Draft a commit summary for the current workspace changes.");
        builder.AppendLine("Do not modify files and do not run git commands that write state.");
        builder.AppendLine("Inspect relevant files if the diff is incomplete, especially untracked files.");
        builder.AppendLine("Return:");
        builder.AppendLine("1. A concise commit title, imperative mood, 72 characters or fewer.");
        builder.AppendLine("2. A short commit body with the main behavior changes.");
        builder.AppendLine("3. A PR description draft with Summary and Verification sections.");
        builder.AppendLine("4. Any notable risk or follow-up, only if real.");
        builder.AppendLine();
        builder.AppendLine("Git status:");
        builder.AppendLine(status.DisplayOutput);
        builder.AppendLine();
        builder.AppendLine("Git diff stat:");
        builder.AppendLine(diffStat.DisplayOutput);
        builder.AppendLine();
        builder.AppendLine("Git diff:");
        builder.AppendLine(diff);
        return builder.ToString().TrimEnd();
    }

    public static string BuildCheckpointDisplayText(AgentCheckpoint checkpoint)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Checkpoint: {checkpoint.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Workspace: {checkpoint.WorkspaceRoot}");
        builder.AppendLine($"Status: {checkpoint.StatusText}");

        if (!string.IsNullOrWhiteSpace(checkpoint.PendingInput))
        {
            builder.AppendLine();
            builder.AppendLine("Pending input:");
            builder.AppendLine(Truncate(checkpoint.PendingInput, 1000));
        }

        if (!string.IsNullOrWhiteSpace(checkpoint.GitStatus))
        {
            builder.AppendLine();
            builder.AppendLine("Git status:");
            builder.AppendLine(checkpoint.GitStatus);
        }

        if (checkpoint.RunSteps.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Recent run steps:");
            foreach (var step in checkpoint.RunSteps.TakeLast(8))
            {
                builder.AppendLine($"- {step.State}: {step.Title}");
            }
        }

        if (checkpoint.PlanItems.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Plan:");
            foreach (var item in checkpoint.PlanItems.OrderBy(item => item.Order))
            {
                builder.AppendLine($"- [{GetPlanMark(item.Status)}] {item.Order}. {item.Title}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string GetPlanMark(AgentPlanItemStatus status)
    {
        return status switch
        {
            AgentPlanItemStatus.Done => "x",
            AgentPlanItemStatus.InProgress => "-",
            AgentPlanItemStatus.Blocked => "!",
            _ => " "
        };
    }

    public static string Truncate(string value, int maxLength, string marker = "[truncated]")
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + Environment.NewLine + marker;
    }
}
