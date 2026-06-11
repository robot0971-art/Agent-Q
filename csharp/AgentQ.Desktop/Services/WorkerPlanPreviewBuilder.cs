namespace AgentQ.Desktop.Services;

public sealed class WorkerPlanPreviewBuilder(
    WorkerPlanApprovalSummaryBuilder? summaryBuilder = null,
    WorkerPlanValidator? validator = null)
{
    private readonly WorkerPlanApprovalSummaryBuilder _summaryBuilder = summaryBuilder ?? new WorkerPlanApprovalSummaryBuilder();
    private readonly WorkerPlanValidator _validator = validator ?? new WorkerPlanValidator();

    public WorkerPlanPreview Build(
        WorkerPlan plan,
        string workspaceRoot,
        IEnumerable<string>? projectAllowedCommands = null)
    {
        var summary = _summaryBuilder.Build(plan);
        var validation = _validator.Validate(plan, workspaceRoot, projectAllowedCommands);
        var state = DetermineState(summary, validation);

        return new WorkerPlanPreview
        {
            Plan = plan,
            ApprovalSummary = summary,
            Validation = validation,
            ApprovalState = state,
            DecisionSummary = BuildDecisionSummary(plan, summary, validation, state)
        };
    }

    private static WorkerPlanApprovalState DetermineState(
        WorkerPlanApprovalSummary summary,
        WorkerPlanValidationResult validation)
    {
        if (!validation.IsValid)
        {
            return WorkerPlanApprovalState.Blocked;
        }

        return validation.RequiresApproval || summary.HasHighRiskChanges
            ? WorkerPlanApprovalState.NeedsApproval
            : WorkerPlanApprovalState.Ready;
    }

    private static string BuildDecisionSummary(
        WorkerPlan plan,
        WorkerPlanApprovalSummary summary,
        WorkerPlanValidationResult validation,
        WorkerPlanApprovalState state)
    {
        var title = string.IsNullOrWhiteSpace(plan.Summary)
            ? string.IsNullOrWhiteSpace(plan.Goal) ? "Worker plan" : plan.Goal
            : plan.Summary;
        var changes = $"{summary.CreateCount:0} create, {summary.ModifyCount:0} modify, {summary.DeleteCount:0} delete, {summary.RunCommandCount:0} run";
        var risk = $"Risk: {summary.RiskLevel}";
        var verification = summary.VerificationCommands.Count == 0
            ? "Verification: none"
            : $"Verification: {string.Join("; ", summary.VerificationCommands.Take(3))}";
        var status = state switch
        {
            WorkerPlanApprovalState.Blocked => $"Blocked: {FirstIssue(validation)}",
            WorkerPlanApprovalState.NeedsApproval => "Approval required",
            _ => "Ready"
        };

        return $"{title} | {changes} | {risk} | {verification} | {status}";
    }

    private static string FirstIssue(WorkerPlanValidationResult validation)
    {
        return validation.Issues.FirstOrDefault(issue => issue.Severity == WorkerPlanValidationSeverity.Blocker)?.Message ??
               validation.Issues.FirstOrDefault()?.Message ??
               "validation failed";
    }
}
