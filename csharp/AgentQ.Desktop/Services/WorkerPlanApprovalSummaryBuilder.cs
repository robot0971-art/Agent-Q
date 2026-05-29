namespace AgentQ.Desktop.Services;

public sealed class WorkerPlanApprovalSummaryBuilder
{
    private static readonly string[] HighRiskPathTerms =
    [
        "auth",
        "login",
        "security",
        "permission",
        "migration",
        "migrations",
        "schema",
        "database",
        "db/"
    ];

    public WorkerPlanApprovalSummary Build(WorkerPlan plan)
    {
        var fileSteps = plan.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.Path))
            .ToList();

        var created = PathsFor(fileSteps, WorkerPlanStepKind.CreateFile);
        var modified = PathsFor(fileSteps, WorkerPlanStepKind.ModifyFile);
        var deleted = PathsFor(fileSteps, WorkerPlanStepKind.DeleteFile);
        var riskReasons = BuildRiskReasons(plan, fileSteps, deleted);
        var riskLevel = DetermineRiskLevel(fileSteps, deleted, riskReasons);

        return new WorkerPlanApprovalSummary
        {
            CreateCount = created.Count,
            ModifyCount = modified.Count,
            DeleteCount = deleted.Count,
            CreatedFiles = created,
            ModifiedFiles = modified,
            DeletedFiles = deleted,
            ExpectedChanges = BuildExpectedChanges(fileSteps),
            RiskLevel = riskLevel,
            RiskReasons = riskReasons,
            VerificationCommands = plan.VerificationCommands
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList()
        };
    }

    private static List<string> PathsFor(IEnumerable<WorkerPlanStep> steps, WorkerPlanStepKind kind)
    {
        return steps
            .Where(step => step.Kind == kind)
            .Select(step => step.Path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static List<string> BuildExpectedChanges(IEnumerable<WorkerPlanStep> steps)
    {
        return steps
            .Where(step => step.Kind is WorkerPlanStepKind.CreateFile or WorkerPlanStepKind.ModifyFile or WorkerPlanStepKind.DeleteFile)
            .Select(step =>
            {
                var change = string.IsNullOrWhiteSpace(step.ExpectedChange)
                    ? step.Reason
                    : step.ExpectedChange;
                return string.IsNullOrWhiteSpace(change)
                    ? $"{FormatKind(step.Kind)} {step.Path}"
                    : $"{step.Path}: {change}";
            })
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static List<string> BuildRiskReasons(
        WorkerPlan plan,
        IReadOnlyCollection<WorkerPlanStep> fileSteps,
        IReadOnlyCollection<string> deleted)
    {
        var reasons = new List<string>();
        if (deleted.Count > 0)
        {
            reasons.Add("Deletes files.");
        }

        foreach (var risk in plan.Risks.Where(risk => !string.IsNullOrWhiteSpace(risk)).Take(6))
        {
            AddUnique(reasons, risk.Trim());
        }

        foreach (var step in fileSteps)
        {
            var combined = $"{step.Path}\n{step.Reason}\n{step.ExpectedChange}";
            if (HighRiskPathTerms.Any(term => combined.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                AddUnique(reasons, $"Touches high-risk area: {step.Path}");
            }

            if (step.RequiresApproval)
            {
                AddUnique(reasons, $"Step requires approval: {step.Path}");
            }
        }

        if (fileSteps.Count(step => step.Kind is WorkerPlanStepKind.CreateFile or WorkerPlanStepKind.ModifyFile or WorkerPlanStepKind.DeleteFile) >= 8)
        {
            AddUnique(reasons, "Touches many files.");
        }

        return reasons.Take(10).ToList();
    }

    private static WorkerPlanRiskLevel DetermineRiskLevel(
        IReadOnlyCollection<WorkerPlanStep> fileSteps,
        IReadOnlyCollection<string> deleted,
        IReadOnlyCollection<string> riskReasons)
    {
        if (deleted.Count > 0 ||
            riskReasons.Any(reason => reason.Contains("high-risk", StringComparison.OrdinalIgnoreCase) ||
                                      reason.Contains("requires approval", StringComparison.OrdinalIgnoreCase)))
        {
            return WorkerPlanRiskLevel.High;
        }

        if (riskReasons.Count > 0 ||
            fileSteps.Count(step => step.Kind is WorkerPlanStepKind.CreateFile or WorkerPlanStepKind.ModifyFile) >= 4)
        {
            return WorkerPlanRiskLevel.Medium;
        }

        return WorkerPlanRiskLevel.Low;
    }

    private static string FormatKind(WorkerPlanStepKind kind)
    {
        return kind switch
        {
            WorkerPlanStepKind.CreateFile => "Create",
            WorkerPlanStepKind.ModifyFile => "Modify",
            WorkerPlanStepKind.DeleteFile => "Delete",
            WorkerPlanStepKind.RunCommand => "Run",
            WorkerPlanStepKind.Verify => "Verify",
            _ => kind.ToString()
        };
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }
}
