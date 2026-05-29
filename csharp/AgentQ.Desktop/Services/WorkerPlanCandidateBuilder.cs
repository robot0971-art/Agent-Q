using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class WorkerPlanCandidateBuilder
{
    private static readonly string[] ApprovalRequiredTerms =
    [
        "auth",
        "security",
        "permission",
        "migration",
        "migrations",
        "schema",
        "database",
        "db/"
    ];

    public IReadOnlyList<WorkerPlan> BuildCandidates(
        string goal,
        string language,
        string framework,
        IEnumerable<WorkerScaffoldRecommendation> recommendations)
    {
        return recommendations
            .Where(recommendation => !string.IsNullOrWhiteSpace(recommendation.Name))
            .Select(recommendation => BuildCandidate(goal, language, framework, recommendation))
            .ToList();
    }

    public WorkerPlan BuildCandidate(
        string goal,
        string language,
        string framework,
        WorkerScaffoldRecommendation recommendation)
    {
        var plan = new WorkerPlan
        {
            Goal = goal,
            Language = language,
            Framework = framework,
            Summary = string.IsNullOrWhiteSpace(recommendation.Description)
                ? recommendation.Name
                : recommendation.Description,
            VerificationCommands = recommendation.VerificationCommands
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        foreach (var file in recommendation.Files.Where(file => !string.IsNullOrWhiteSpace(file)))
        {
            var normalized = NormalizePath(file);
            plan.Steps.Add(new WorkerPlanStep
            {
                Kind = WorkerPlanStepKind.CreateFile,
                Path = normalized,
                Reason = $"Create scaffold file for {recommendation.Name}.",
                ExpectedChange = $"Add {DescribePath(normalized)}.",
                RequiresApproval = RequiresApproval(normalized)
            });
        }

        foreach (var command in plan.VerificationCommands)
        {
            plan.Steps.Add(new WorkerPlanStep
            {
                Kind = WorkerPlanStepKind.Verify,
                Reason = "Run recommended verification after executing the plan.",
                ExpectedChange = command
            });
        }

        AddRisks(plan, recommendation);
        return plan;
    }

    private static void AddRisks(WorkerPlan plan, WorkerScaffoldRecommendation recommendation)
    {
        foreach (var file in recommendation.Files)
        {
            if (RequiresApproval(file))
            {
                plan.Risks.Add($"Review high-risk scaffold path before execution: {NormalizePath(file)}");
            }
        }

        if (recommendation.Files.Count >= 8)
        {
            plan.Risks.Add("Scaffold touches many files.");
        }
    }

    private static bool RequiresApproval(string value)
    {
        return ApprovalRequiredTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    private static string DescribePath(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
        return string.IsNullOrWhiteSpace(directory)
            ? fileName
            : $"{fileName} in {directory}";
    }
}
