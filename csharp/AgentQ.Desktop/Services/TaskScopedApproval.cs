using System.IO;

namespace AgentQ.Desktop.Services;

// These are user-facing choices, not permission-risk levels. A choice never
// overrides ToolPermissionPolicy: blocked paths remain blocked even when the
// user selected FullAccess.
public enum UserApprovalMode
{
    PerAction,
    ThisTask,
    FullAccess
}

public static class UserApprovalModeLabels
{
    public static string DisplayName(UserApprovalMode mode, bool useKoreanUi = true) =>
        (mode, useKoreanUi) switch
        {
            (UserApprovalMode.PerAction, true) => "\uAD8C\uD55C \uC2B9\uC778",
            (UserApprovalMode.ThisTask, true) => "\uC791\uC5C5 \uC804\uCCB4 \uC2B9\uC778",
            (UserApprovalMode.FullAccess, true) => "\uC804\uCCB4 \uC561\uC138\uC2A4",
            (UserApprovalMode.PerAction, false) => "Approve this action",
            (UserApprovalMode.ThisTask, false) => "Approve this task",
            _ => "Full access"
        };

    public static string SafetyDescription(UserApprovalMode mode, bool useKoreanUi = true) =>
        (mode, useKoreanUi) switch
        {
            (UserApprovalMode.PerAction, true) => "Approves only the current action.",
            (UserApprovalMode.ThisTask, true) => "Limited to this task, run, and approved capability. It can be revoked at any time.",
            (UserApprovalMode.FullAccess, true) => "Explicitly permits broader capabilities only for the current run and workspace. Blocked policy paths remain blocked.",
            (UserApprovalMode.PerAction, false) => "Approves only the current action.",
            (UserApprovalMode.ThisTask, false) => "Limited to this task, run, and approved capability. It can be revoked at any time.",
            _ => "Explicitly permits broader capabilities only for the current run and workspace. Blocked policy paths remain blocked."
        };
}

public sealed record TaskScopedApprovalRequest(
    string TaskContractId,
    string RunId,
    string WorkspaceRoot,
    PermissionRiskLevel Capability,
    UserApprovalMode Mode,
    DateTimeOffset ExpiresAt);

public sealed record TaskScopedApproval(
    string ApprovalId,
    string TaskContractId,
    string RunId,
    string WorkspaceRoot,
    IReadOnlySet<PermissionRiskLevel> Capabilities,
    UserApprovalMode Mode,
    DateTimeOffset ExpiresAt,
    DateTimeOffset GrantedAt)
{
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}

/// <summary>
/// Stores explicit, short-lived approvals. Matching is deliberately exact for
/// task, run, and workspace; FullAccess broadens capabilities, not identity.
/// Callers must still evaluate ToolPermissionPolicy before consulting this store.
/// </summary>
public sealed class TaskScopedApprovalStore
{
    private readonly Dictionary<string, TaskScopedApproval> _approvals = new(StringComparer.Ordinal);

    public TaskScopedApproval Grant(TaskScopedApprovalRequest request, DateTimeOffset now)
    {
        ValidateRequest(request, now);
        var capabilities = request.Mode == UserApprovalMode.FullAccess
            ? AllGrantableCapabilities
            : new HashSet<PermissionRiskLevel> { request.Capability };

        var approval = new TaskScopedApproval(
            Guid.NewGuid().ToString("N"),
            request.TaskContractId,
            request.RunId,
            NormalizeWorkspace(request.WorkspaceRoot),
            capabilities,
            request.Mode,
            request.ExpiresAt,
            now);
        _approvals[approval.ApprovalId] = approval;
        return approval;
    }

    public bool IsApproved(string taskContractId, string runId, string workspaceRoot, PermissionRiskLevel capability, DateTimeOffset now)
    {
        RemoveExpired(now);
        var workspace = NormalizeWorkspace(workspaceRoot);
        return _approvals.Values.Any(approval =>
            string.Equals(approval.TaskContractId, taskContractId, StringComparison.Ordinal) &&
            string.Equals(approval.RunId, runId, StringComparison.Ordinal) &&
            string.Equals(approval.WorkspaceRoot, workspace, StringComparison.OrdinalIgnoreCase) &&
            approval.Capabilities.Contains(capability));
    }

    public bool HasFullAccess(string taskContractId, string runId, string workspaceRoot, DateTimeOffset now)
    {
        RemoveExpired(now);
        var workspace = NormalizeWorkspace(workspaceRoot);
        return _approvals.Values.Any(approval =>
            approval.Mode == UserApprovalMode.FullAccess &&
            string.Equals(approval.TaskContractId, taskContractId, StringComparison.Ordinal) &&
            string.Equals(approval.RunId, runId, StringComparison.Ordinal) &&
            string.Equals(approval.WorkspaceRoot, workspace, StringComparison.OrdinalIgnoreCase));
    }

    public bool Revoke(string approvalId) => _approvals.Remove(approvalId);

    public void RevokeRun(string runId)
    {
        foreach (var approvalId in _approvals.Values.Where(approval => string.Equals(approval.RunId, runId, StringComparison.Ordinal)).Select(approval => approval.ApprovalId).ToArray())
            _approvals.Remove(approvalId);
    }

    public void RemoveExpired(DateTimeOffset now)
    {
        foreach (var approvalId in _approvals.Values.Where(approval => approval.IsExpired(now)).Select(approval => approval.ApprovalId).ToArray())
            _approvals.Remove(approvalId);
    }

    private static readonly IReadOnlySet<PermissionRiskLevel> AllGrantableCapabilities = new HashSet<PermissionRiskLevel>
    {
        PermissionRiskLevel.LowRiskProjectWrite,
        PermissionRiskLevel.ProjectWrite,
        PermissionRiskLevel.VerificationCommand,
        PermissionRiskLevel.ShellCommand,
        PermissionRiskLevel.Network,
        PermissionRiskLevel.GitWrite
    };

    private static void ValidateRequest(TaskScopedApprovalRequest request, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TaskContractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceRoot);
        if (request.ExpiresAt <= now)
            throw new ArgumentOutOfRangeException(nameof(request), "An approval must expire in the future.");
        if (request.Capability is PermissionRiskLevel.ExternalWrite or PermissionRiskLevel.Destructive)
            throw new ArgumentException("Blocked capabilities cannot be granted.", nameof(request));
    }

    private static string NormalizeWorkspace(string workspaceRoot) =>
        Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
