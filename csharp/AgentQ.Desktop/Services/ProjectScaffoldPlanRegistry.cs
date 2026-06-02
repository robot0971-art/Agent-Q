using System.Collections.Concurrent;
using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class ProjectScaffoldPlanRegistry
{
    private readonly ConcurrentDictionary<string, ProjectScaffoldPlanRecord> _plans = new(StringComparer.Ordinal);

    public ProjectScaffoldPlanningResult Register(ProjectScaffoldPlanningResult result, string workspaceRoot)
    {
        if (!result.CanProceed || result.Intent == null || result.Plan == null)
        {
            return result;
        }

        var record = Register(result.Intent, result.Plan, workspaceRoot);
        return new ProjectScaffoldPlanningResult
        {
            IsGreenfieldRequest = result.IsGreenfieldRequest,
            CanProceed = result.CanProceed,
            ClarifyingQuestion = result.ClarifyingQuestion,
            Intent = result.Intent,
            Plan = result.Plan,
            PlanId = record.PlanId,
            PlanHash = record.PlanHash,
            Reasons = result.Reasons
        };
    }

    public ProjectScaffoldPlanRecord Register(ProjectScaffoldIntentModel intent, ProjectScaffoldPlanModel plan, string workspaceRoot)
    {
        var record = new ProjectScaffoldPlanRecord(
            PlanId: "psc_" + Guid.NewGuid().ToString("N"),
            WorkspaceRoot: NormalizeWorkspaceRoot(workspaceRoot),
            Intent: CloneIntent(intent),
            Plan: ClonePlan(plan),
            PlanHash: ProjectScaffoldPlanner.ComputePlanHash(intent, plan),
            CreatedAtUtc: DateTimeOffset.UtcNow);
        _plans[record.PlanId] = record;
        return record;
    }

    public bool TryGet(string planId, out ProjectScaffoldPlanRecord record)
    {
        if (!string.IsNullOrWhiteSpace(planId) &&
            _plans.TryGetValue(planId.Trim(), out var found))
        {
            record = found;
            return true;
        }

        record = null!;
        return false;
    }

    public static bool MatchesWorkspace(string registeredWorkspaceRoot, string workspaceRoot)
    {
        var registered = NormalizeWorkspaceRoot(registeredWorkspaceRoot);
        var current = NormalizeWorkspaceRoot(workspaceRoot);
        return !string.IsNullOrWhiteSpace(registered) &&
               !string.IsNullOrWhiteSpace(current) &&
               string.Equals(registered, current, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeWorkspaceRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(workspaceRoot.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return workspaceRoot.Trim();
        }
    }

    private static ProjectScaffoldIntentModel CloneIntent(ProjectScaffoldIntentModel intent) => new()
    {
        ProjectType = intent.ProjectType,
        Language = intent.Language,
        Framework = intent.Framework,
        Style = intent.Style
    };

    private static ProjectScaffoldPlanModel ClonePlan(ProjectScaffoldPlanModel plan) => new()
    {
        Name = plan.Name,
        Files = plan.Files.ToList(),
        VerificationCommands = plan.VerificationCommands.ToList()
    };
}

public sealed record ProjectScaffoldPlanRecord(
    string PlanId,
    string WorkspaceRoot,
    ProjectScaffoldIntentModel Intent,
    ProjectScaffoldPlanModel Plan,
    string PlanHash,
    DateTimeOffset CreatedAtUtc);
