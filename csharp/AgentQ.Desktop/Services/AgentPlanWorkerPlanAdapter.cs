using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public sealed class AgentPlanWorkerPlanAdapter
{
    private static readonly Regex PathRegex = new(
        @"(?<path>[\w./\\-]+\.(?:cs|xaml|csproj|sln|ts|tsx|js|jsx|mjs|cjs|py|sql|json|yml|yaml|md|rs|go|java|kt|swift|php|r))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] HighRiskTerms =
    [
        "auth",
        "login",
        "security",
        "permission",
        "migration",
        "schema",
        "database",
        "db/"
    ];

    public WorkerPlan Convert(
        IReadOnlyList<AgentPlanItem> items,
        string goal,
        IReadOnlyList<string> workspaceVerificationCommands)
    {
        var plan = new WorkerPlan
        {
            Goal = goal,
            Summary = string.IsNullOrWhiteSpace(goal) ? "Captured desktop plan" : goal,
            Steps = items.SelectMany(ToSteps).ToList()
        };

        foreach (var command in InferVerificationCommands(items, workspaceVerificationCommands).Take(4))
        {
            plan.VerificationCommands.Add(command);
        }

        if (plan.Steps.Any(step => step.RequiresApproval))
        {
            plan.Risks.Add("Plan contains high-risk files or behavior that should be approved before execution.");
        }

        return plan;
    }

    private static IEnumerable<WorkerPlanStep> ToSteps(AgentPlanItem item)
    {
        var text = $"{item.Title} {item.Detail}";
        var paths = PathRegex.Matches(text)
            .Select(match => match.Groups["path"].Value.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
        {
            yield return new WorkerPlanStep
            {
                Kind = WorkerPlanStepKind.RunCommand,
                Reason = item.Title,
                ExpectedChange = item.Detail
            };
            yield break;
        }

        foreach (var path in paths)
        {
            yield return new WorkerPlanStep
            {
                Kind = InferStepKind(text),
                Path = path,
                Reason = item.Title,
                ExpectedChange = string.IsNullOrWhiteSpace(item.Detail) ? item.Title : item.Detail,
                RequiresApproval = IsHighRisk(text) || IsHighRisk(path)
            };
        }
    }

    private static WorkerPlanStepKind InferStepKind(string text)
    {
        if (ContainsAny(text, "delete", "remove"))
        {
            return WorkerPlanStepKind.DeleteFile;
        }

        if (ContainsAny(text, "create", "add", "new"))
        {
            return WorkerPlanStepKind.CreateFile;
        }

        return WorkerPlanStepKind.ModifyFile;
    }

    private static IEnumerable<string> InferVerificationCommands(
        IReadOnlyList<AgentPlanItem> items,
        IReadOnlyList<string> workspaceVerificationCommands)
    {
        var text = string.Join(' ', items.Select(item => $"{item.Title} {item.Detail}"));
        foreach (var command in workspaceVerificationCommands)
        {
            if (ShouldUseCommand(text, command))
            {
                yield return command;
            }
        }
    }

    private static bool ShouldUseCommand(string planText, string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        return ContainsAny(planText, "test", "verify", "playwright", "build", "검증", "테스트") ||
               ContainsAny(command, "test", "build", "playwright");
    }

    private static bool IsHighRisk(string value)
    {
        return HighRiskTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
