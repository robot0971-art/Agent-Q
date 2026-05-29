namespace AgentQ.Desktop.Services;

public sealed class WorkerExecutionPipeline(
    WorkerPlanPreviewBuilder previewBuilder,
    AutoFixLoopGuard loopGuard,
    WorkerScaffoldExecutor? scaffoldExecutor = null)
{
    private readonly WorkerScaffoldExecutor _scaffoldExecutor = scaffoldExecutor ?? new WorkerScaffoldExecutor();

    public WorkerExecutionContext Begin(
        WorkerPlan plan,
        string workspaceRoot,
        IEnumerable<string>? projectAllowedCommands = null)
    {
        var preview = previewBuilder.Build(plan, workspaceRoot, projectAllowedCommands);
        var context = new WorkerExecutionContext
        {
            Plan = plan,
            Preview = preview,
            State = ToExecutionState(preview.ApprovalState),
            StatusMessage = preview.DecisionSummary
        };

        foreach (var verificationPlan in BuildVerificationPlans(plan))
        {
            context.VerificationPlans.Add(verificationPlan);
        }

        return context;
    }

    public bool Approve(WorkerExecutionContext context)
    {
        if (context.State != WorkerExecutionState.AwaitingApproval)
        {
            return false;
        }

        context.State = WorkerExecutionState.Ready;
        context.StatusMessage = "Worker plan approved.";
        return true;
    }

    public async Task<WorkerScaffoldExecutionResult> ExecuteScaffoldAsync(
        WorkerExecutionContext context,
        string workspaceRoot,
        string featureName,
        CancellationToken ct = default)
    {
        if (context.State != WorkerExecutionState.Ready)
        {
            var blocked = new WorkerScaffoldExecutionResult
            {
                Succeeded = false,
                Issues = [$"Worker plan is not ready for scaffold execution: {context.State}"]
            };
            context.ScaffoldResult = blocked;
            context.State = WorkerExecutionState.ScaffoldFailed;
            context.StatusMessage = blocked.Issues[0];
            return blocked;
        }

        var result = await _scaffoldExecutor.ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                Plan = context.Plan,
                WorkspaceRoot = workspaceRoot,
                FeatureName = featureName
            },
            ct);
        context.ScaffoldResult = result;
        context.State = result.Succeeded
            ? WorkerExecutionState.ScaffoldExecuted
            : WorkerExecutionState.ScaffoldFailed;
        context.StatusMessage = result.Succeeded
            ? $"Worker scaffold created {result.CreatedFiles.Count:0} file(s)."
            : $"Worker scaffold failed: {string.Join("; ", result.Issues.Take(3))}";
        return result;
    }

    public void ApplyVerificationResult(
        WorkerExecutionContext context,
        DesktopVerificationWorkflowResult verificationResult)
    {
        if (verificationResult.Succeeded)
        {
            context.State = WorkerExecutionState.Succeeded;
            context.RepairPlan = null;
            context.StatusMessage = "Worker verification passed.";
            return;
        }

        var signature = BuildFailureSignature(verificationResult);
        var decision = loopGuard.RecordFailure(context.LoopGuardState, signature);
        context.LoopGuardState = decision.State;
        if (decision.ShouldStop)
        {
            context.State = WorkerExecutionState.StoppedRepeatedFailure;
            context.StatusMessage = decision.Message;
            return;
        }

        context.RepairPlan = BuildRepairPlan(context.Plan, verificationResult, signature);
        context.State = WorkerExecutionState.RepairRequired;
        context.StatusMessage = "Worker repair required after failed verification.";
    }

    private static WorkerExecutionState ToExecutionState(WorkerPlanApprovalState state)
    {
        return state switch
        {
            WorkerPlanApprovalState.Blocked => WorkerExecutionState.Blocked,
            WorkerPlanApprovalState.NeedsApproval => WorkerExecutionState.AwaitingApproval,
            _ => WorkerExecutionState.Ready
        };
    }

    private static IEnumerable<AgentVerificationPlan> BuildVerificationPlans(WorkerPlan plan)
    {
        return plan.VerificationCommands
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(command => new AgentVerificationPlan
            {
                Title = "Worker verification",
                Command = command,
                Reason = string.IsNullOrWhiteSpace(plan.Goal)
                    ? "Verify worker plan changes."
                    : $"Verify worker plan: {plan.Goal}"
            });
    }

    private static WorkerRepairPlan BuildRepairPlan(
        WorkerPlan plan,
        DesktopVerificationWorkflowResult verificationResult,
        string signature)
    {
        return new WorkerRepairPlan
        {
            Goal = $"Repair failed worker plan: {plan.Goal}",
            Language = plan.Language,
            Framework = plan.Framework,
            Summary = verificationResult.FailureAnalysis?.Summary ?? verificationResult.FailureSummary,
            FailureSignature = signature,
            FailureKind = verificationResult.FailureAnalysis?.Kind.ToString() ?? "Unknown",
            SuggestedNextStep = verificationResult.FailureAnalysis?.SuggestedNextStep ?? "Inspect the failed verification output before editing.",
            Evidence = verificationResult.FailureAnalysis?.Evidence.ToList() ?? [],
            VerificationCommands = plan.VerificationCommands.ToList(),
            Risks = ["Repair should stay scoped to the failed verification evidence."]
        };
    }

    private static string BuildFailureSignature(DesktopVerificationWorkflowResult verificationResult)
    {
        var title = verificationResult.FailureAnalysis?.Title ?? "Unknown";
        var summary = string.IsNullOrWhiteSpace(verificationResult.FailureSummary)
            ? verificationResult.RunResult?.CombinedOutput ?? string.Empty
            : verificationResult.FailureSummary;
        summary = summary.ReplaceLineEndings(" ").Trim();
        while (summary.Contains("  ", StringComparison.Ordinal))
        {
            summary = summary.Replace("  ", " ", StringComparison.Ordinal);
        }

        return $"{title}|{(summary.Length <= 240 ? summary : summary[..240])}";
    }
}
