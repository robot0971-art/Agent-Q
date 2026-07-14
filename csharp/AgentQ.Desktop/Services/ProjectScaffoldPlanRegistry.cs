using System.Collections.Concurrent;
using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class ProjectScaffoldPlanRegistry
{
    private readonly ConcurrentDictionary<string, ProjectScaffoldPlanRecord> _plans = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ScaffoldAuthorization> _authorizations = new(StringComparer.Ordinal);

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

    public ScaffoldAuthorization IssueAuthorization(
        ProjectScaffoldPlanRecord record,
        bool overwriteExistingFiles,
        string? taskContractId = null,
        string? runId = null,
        TimeSpan? lifetime = null)
    {
        var files = record.Plan.Files.Select(NormalizeRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var targetRoot = DetermineTargetRoot(record.WorkspaceRoot, files);
        var authorization = new ScaffoldAuthorization(
            ScaffoldAuthorizationId: "sca_" + Guid.NewGuid().ToString("N"),
            PlanId: record.PlanId,
            PlanHash: record.PlanHash,
            WorkspaceRoot: record.WorkspaceRoot,
            TargetRoot: targetRoot,
            AllowedFiles: files,
            AllowedPathPatterns: BuildAllowedPathPatterns(files),
            AllowedCommands: record.Plan.VerificationCommands.Where(command => VerificationCommandPolicy.IsAllowed(command)).Select(command => command.Trim()).Distinct(StringComparer.Ordinal).ToArray(),
            AllowCreateDirectories: true,
            AllowDependencyInstall: record.Plan.VerificationCommands.Any(command => command.Trim().Equals("npm install", StringComparison.OrdinalIgnoreCase) || command.Trim().Equals("dotnet restore", StringComparison.OrdinalIgnoreCase)),
            AllowVerification: true,
            AllowRuntimePreview: record.Plan.VerificationCommands.Any(command => command.Contains(" dev", StringComparison.OrdinalIgnoreCase) || command.Contains(" preview", StringComparison.OrdinalIgnoreCase)),
            OverwriteExistingFiles: overwriteExistingFiles,
            Expiry: DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(30)),
            TaskContractId: taskContractId?.Trim() ?? string.Empty,
            RunId: runId?.Trim() ?? string.Empty,
            AuthorizationEvidence: $"approved deterministic scaffold plan {record.PlanId} ({record.PlanHash})");
        _authorizations[authorization.ScaffoldAuthorizationId] = authorization;
        return authorization;
    }

    public bool TryGetValidAuthorization(string planId, string planHash, out ScaffoldAuthorization authorization)
    {
        authorization = _authorizations.Values
            .Where(item => item.PlanId.Equals(planId, StringComparison.Ordinal) &&
                           item.PlanHash.Equals(planHash, StringComparison.OrdinalIgnoreCase) &&
                           item.Expiry > DateTimeOffset.UtcNow)
            .OrderByDescending(item => item.Expiry)
            .FirstOrDefault()!;
        return authorization != null;
    }

    public bool TryGetValidAuthorization(
        string scaffoldAuthorizationId,
        string planId,
        string planHash,
        string workspaceRoot,
        string? taskContractId,
        string? runId,
        out ScaffoldAuthorization authorization)
    {
        authorization = null!;
        if (string.IsNullOrWhiteSpace(scaffoldAuthorizationId) ||
            !_authorizations.TryGetValue(scaffoldAuthorizationId.Trim(), out var candidate) ||
            candidate.Expiry <= DateTimeOffset.UtcNow ||
            !string.Equals(candidate.PlanId, planId, StringComparison.Ordinal) ||
            !string.Equals(candidate.PlanHash, planHash, StringComparison.OrdinalIgnoreCase) ||
            !MatchesWorkspace(candidate.WorkspaceRoot, workspaceRoot))
        {
            return false;
        }

        if (!MatchesOptionalBinding(candidate.TaskContractId, taskContractId) ||
            !MatchesOptionalBinding(candidate.RunId, runId))
        {
            return false;
        }

        authorization = candidate;
        return true;
    }

    public void RevokeAuthorization(string scaffoldAuthorizationId)
    {
        if (!string.IsNullOrWhiteSpace(scaffoldAuthorizationId))
        {
            _authorizations.TryRemove(scaffoldAuthorizationId.Trim(), out _);
        }
    }

    public bool TryAuthorizeFile(ScaffoldAuthorization authorization, string relativePath)
    {
        if (authorization.Expiry <= DateTimeOffset.UtcNow || !TryNormalizeSafeRelativePath(relativePath, out var normalized))
        {
            return false;
        }

        return authorization.AllowedFiles.Contains(normalized, StringComparer.OrdinalIgnoreCase) ||
               authorization.AllowedPathPatterns.Any(pattern => MatchesAllowedPathPattern(pattern, normalized));
    }

    public bool TryAuthorizeCommand(ScaffoldAuthorization authorization, string command) =>
        authorization.Expiry > DateTimeOffset.UtcNow &&
        authorization.AllowVerification &&
        authorization.AllowedCommands.Contains(command.Trim(), StringComparer.Ordinal);

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

    private static string NormalizeRelativePath(string value) => value.Trim().Replace('\\', '/');

    private static bool MatchesOptionalBinding(string authorizationBinding, string? requestedBinding)
    {
        var requested = requestedBinding?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(requested) ||
               string.Equals(authorizationBinding, requested, StringComparison.Ordinal);
    }

    private static bool TryNormalizeSafeRelativePath(string value, out string normalized)
    {
        normalized = NormalizeRelativePath(value);
        if (string.IsNullOrWhiteSpace(normalized) ||
            Path.IsPathRooted(value) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(":", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Any(segment => segment is "." or ".." ||
                                    segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                                    segment.Equals(".agentq", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    private static bool MatchesAllowedPathPattern(string pattern, string relativePath)
    {
        var normalizedPattern = NormalizeRelativePath(pattern);
        if (!normalizedPattern.EndsWith("/**", StringComparison.Ordinal) ||
            !TryNormalizeSafeRelativePath(normalizedPattern[..^3], out var prefix))
        {
            return false;
        }

        return relativePath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetermineTargetRoot(string workspaceRoot, IReadOnlyList<string> files)
    {
        var topLevel = files.Select(file => file.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return topLevel.Length == 1 && files.All(file => file.Contains('/'))
            ? Path.Combine(workspaceRoot, topLevel[0])
            : workspaceRoot;
    }

    private static IReadOnlyList<string> BuildAllowedPathPatterns(IReadOnlyList<string> files) => files
        .Where(file => file.Contains('/'))
        .Select(file => file[..(file.LastIndexOf('/') + 1)] + "**")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

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

public sealed record ScaffoldAuthorization(
    string ScaffoldAuthorizationId,
    string PlanId,
    string PlanHash,
    string WorkspaceRoot,
    string TargetRoot,
    IReadOnlyList<string> AllowedFiles,
    IReadOnlyList<string> AllowedPathPatterns,
    IReadOnlyList<string> AllowedCommands,
    bool AllowCreateDirectories,
    bool AllowDependencyInstall,
    bool AllowVerification,
    bool AllowRuntimePreview,
    bool OverwriteExistingFiles,
    DateTimeOffset Expiry,
    string TaskContractId,
    string RunId,
    string AuthorizationEvidence);
