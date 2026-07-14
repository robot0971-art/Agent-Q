using System.Windows;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPermissionEnforcer : IPermissionEnforcer
{
    private static readonly TimeSpan TaskApprovalLifetime = TimeSpan.FromHours(8);
    private readonly Window _owner;
    private readonly AgentWorkMode _workMode;
    private readonly bool _useKoreanUi;
    private readonly string _workspaceRoot;
    private readonly TaskScopedApprovalStore _taskApprovals;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly HashSet<PermissionRiskLevel> _approvedForRun = [];

    /// <summary>
    /// Creates a permission enforcer whose reusable approvals are confined to
    /// one task, one run, and one normalized workspace. The optional identity
    /// arguments exist for callers that already own a TaskContract/Run; until
    /// every workflow supplies them, generated immutable identities preserve
    /// the same isolation.
    /// </summary>
    public DesktopPermissionEnforcer(
        Window owner,
        AgentWorkMode workMode,
        bool useKoreanUi = false,
        string workspaceRoot = "",
        string? taskContractId = null,
        string? runId = null,
        TaskScopedApprovalStore? taskApprovals = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _owner = owner;
        _workMode = workMode;
        _useKoreanUi = useKoreanUi;
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? Environment.CurrentDirectory
            : workspaceRoot;
        TaskContractId = string.IsNullOrWhiteSpace(taskContractId) ? Guid.NewGuid().ToString("N") : taskContractId;
        RunId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId;
        _taskApprovals = taskApprovals ?? new TaskScopedApprovalStore();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string TaskContractId { get; }

    public string RunId { get; }

    public event Action<IReadOnlyCollection<PermissionRiskLevel>>? ApprovedForRunChanged;

    public event Action<DesktopPermissionEvent>? PermissionEventRecorded;

    public IReadOnlyCollection<PermissionRiskLevel> ApprovedForRun => _approvedForRun.ToArray();

    public async Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson)
    {
        return await _owner.Dispatcher.InvokeAsync(() =>
        {
            // Policy is always evaluated first. A task approval is never a way
            // to override a blocked/destructive/external operation.
            var policy = ToolPermissionPolicy.Evaluate(toolName, inputJson, _workspaceRoot, _workMode);
            var assessment = policy.Assessment;
            if (policy.IsBlocked)
            {
                RecordPermissionEvent("Blocked", toolName, assessment, PermissionApprovalChoice.Deny);
                System.Windows.MessageBox.Show(
                    _owner,
                    BuildPermissionBlockedMessage(assessment, _workMode, policy.PolicyReason, _useKoreanUi),
                    DesktopLocalizer.PermissionBlockedTitle(_useKoreanUi),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            if (policy.Decision == ToolPermissionDecision.Allow)
            {
                RecordPermissionEvent("Allowed by policy", toolName, assessment, null);
                return true;
            }

            var reusableApprovalAllowed = IsReusableApproval(toolName, assessment.RiskLevel);
            var fullAccessAllowed = IsFullAccessApprovalTool(toolName);
            if ((reusableApprovalAllowed && IsApprovedForCurrentTask(assessment.RiskLevel)) ||
                (fullAccessAllowed && HasFullAccessForCurrentTask()))
            {
                RecordPermissionEvent("Allowed by task approval", toolName, assessment, null);
                return true;
            }

            var preview = inputJson.Length > 1400
                ? inputJson[..1400] + Environment.NewLine + "...(truncated)"
                : inputJson;
            var focusedPreview = BuildFocusedPreview(toolName, inputJson);
            var approvalHint = reusableApprovalAllowed
                ? DesktopLocalizer.ReusableApprovalHint(_useKoreanUi)
                : string.Empty;
            var dialogContent = new PermissionDialogContent(
                BuildPermissionSummary(assessment, _useKoreanUi),
                $"{assessment.RiskLevel} / {assessment.Operation}",
                assessment.Target,
                assessment.Reason + approvalHint,
                policy.PolicyReason,
                toolName,
                description,
                focusedPreview.Trim(),
                preview);

            var choice = PermissionApprovalDialog.Show(
                _owner,
                $"AgentQ permission: {assessment.RiskLevel}",
                dialogContent,
                reusableApprovalAllowed,
                fullAccessAllowed,
                _useKoreanUi);

            var approvalMode = choice switch
            {
                PermissionApprovalChoice.AllowSimilarForRun => UserApprovalMode.ThisTask,
                PermissionApprovalChoice.AllowAllForRun => UserApprovalMode.FullAccess,
                _ => (UserApprovalMode?)null
            };
            if (approvalMode is UserApprovalMode.ThisTask && !reusableApprovalAllowed)
            {
                approvalMode = null;
            }

            if (approvalMode is UserApprovalMode.FullAccess && !fullAccessAllowed)
            {
                approvalMode = null;
            }

            if (approvalMode is { } mode)
            {
                var approval = _taskApprovals.Grant(new TaskScopedApprovalRequest(
                    TaskContractId,
                    RunId,
                    _workspaceRoot,
                    assessment.RiskLevel,
                    mode,
                    _utcNow().Add(TaskApprovalLifetime)), _utcNow());
                foreach (var approvedRisk in approval.Capabilities)
                {
                    _approvedForRun.Add(approvedRisk);
                }
            }

            ApprovedForRunChanged?.Invoke(ApprovedForRun);
            RecordPermissionEvent(
                choice == PermissionApprovalChoice.Deny ? "Denied" : "Approved",
                toolName,
                assessment,
                choice);

            return choice is PermissionApprovalChoice.AllowOnce
                or PermissionApprovalChoice.AllowSimilarForRun
                or PermissionApprovalChoice.AllowAllForRun;
        });
    }

    public void ClearRunApprovals()
    {
        _approvedForRun.Clear();
        _taskApprovals.RevokeRun(RunId);
        ApprovedForRunChanged?.Invoke(ApprovedForRun);
    }

    public bool IsApprovedForCurrentTask(PermissionRiskLevel capability) =>
        _taskApprovals.IsApproved(TaskContractId, RunId, _workspaceRoot, capability, _utcNow());

    public bool HasFullAccessForCurrentTask() =>
        _taskApprovals.HasFullAccess(TaskContractId, RunId, _workspaceRoot, _utcNow());

    private void RecordPermissionEvent(
        string outcome,
        string toolName,
        ToolPermissionAssessment assessment,
        PermissionApprovalChoice? choice)
    {
        PermissionEventRecorded?.Invoke(new DesktopPermissionEvent(
            outcome,
            toolName,
            assessment.RiskLevel,
            assessment.Operation,
            assessment.Target,
            choice));
    }

    public static IReadOnlyList<PermissionRiskLevel> GetReusableApprovals(
        PermissionApprovalChoice choice,
        PermissionRiskLevel currentRiskLevel)
        => GetReusableApprovals(choice, currentRiskLevel, toolName: string.Empty);

    public static IReadOnlyList<PermissionRiskLevel> GetReusableApprovals(
        PermissionApprovalChoice choice,
        PermissionRiskLevel currentRiskLevel,
        string toolName)
    {
        if (!IsReusableApproval(toolName, currentRiskLevel))
        {
            return [];
        }

        if (choice == PermissionApprovalChoice.AllowSimilarForRun &&
            IsReusableApproval(toolName, currentRiskLevel))
        {
            return [currentRiskLevel];
        }

        if (choice == PermissionApprovalChoice.AllowAllForRun)
        {
            return [PermissionRiskLevel.ProjectWrite, PermissionRiskLevel.VerificationCommand];
        }

        return [];
    }

    public static string FormatApprovedForRun(IReadOnlyCollection<PermissionRiskLevel> riskLevels)
    {
        if (riskLevels.Count == 0)
        {
            return "Run permissions: none";
        }

        var labels = riskLevels
            .OrderBy(risk => risk)
            .Select(risk => risk switch
            {
                PermissionRiskLevel.LowRiskProjectWrite => "new empty files/folders",
                PermissionRiskLevel.ProjectWrite => "workspace edits",
                PermissionRiskLevel.VerificationCommand => "build/test",
                _ => risk.ToString()
            });
        return $"Run permissions: {string.Join(", ", labels)}";
    }

    public static string FormatPermissionEvent(DesktopPermissionEvent permissionEvent)
    {
        var choiceText = permissionEvent.Choice == null
            ? string.Empty
            : $" ({permissionEvent.Choice})";
        var target = string.IsNullOrWhiteSpace(permissionEvent.Target)
            ? "(no target)"
            : permissionEvent.Target;

        return $"{permissionEvent.Outcome}{choiceText}: {permissionEvent.RiskLevel} {permissionEvent.ToolName} -> {target}";
    }

    public static string BuildPermissionSummary(ToolPermissionAssessment assessment, bool useKoreanUi = true) =>
        DesktopLocalizer.PermissionSummary(assessment, useKoreanUi);

    public static string BuildPermissionBlockedMessage(
        ToolPermissionAssessment assessment,
        AgentWorkMode workMode,
        string policyReason,
        bool useKoreanUi = true) =>
        DesktopLocalizer.PermissionBlockedMessage(assessment, workMode, policyReason, useKoreanUi);

    private static bool IsReusableApproval(string toolName, PermissionRiskLevel riskLevel)
    {
        return !IsPlanSpecificApprovalTool(toolName) &&
               (riskLevel is PermissionRiskLevel.ProjectWrite or PermissionRiskLevel.VerificationCommand);
    }

    private static bool IsFullAccessApprovalTool(string toolName) =>
        !IsPlanSpecificApprovalTool(toolName);

    private static bool IsPlanSpecificApprovalTool(string toolName) =>
        string.Equals(toolName, "create_project_scaffold", StringComparison.OrdinalIgnoreCase);

    private static string BuildFocusedPreview(string toolName, string inputJson)
    {
        var input = DesktopToolInputParser.Parse(inputJson);
        if (input.Count == 0)
        {
            return string.Empty;
        }

        if (string.Equals(toolName, "write_file", StringComparison.Ordinal))
        {
            var path = GetString(input, "path");
            var content = GetString(input, "content");
            return
                $"File mutation preview:{Environment.NewLine}" +
                $"Path: {Fallback(path, "(missing path)")}{Environment.NewLine}" +
                $"Write length: {content.Length} characters{Environment.NewLine}" +
                $"Content preview:{Environment.NewLine}{TrimPreview(content, 1000)}{Environment.NewLine}{Environment.NewLine}";
        }

        if (string.Equals(toolName, "edit_file", StringComparison.Ordinal))
        {
            var path = GetString(input, "path");
            var oldString = GetString(input, "old_string");
            var newString = GetString(input, "new_string");
            var replaceAll = input.TryGetValue("replace_all", out var rawReplaceAll) && rawReplaceAll is true;
            return
                $"File mutation preview:{Environment.NewLine}" +
                $"Path: {Fallback(path, "(missing path)")}{Environment.NewLine}" +
                $"Replace all: {replaceAll}{Environment.NewLine}" +
                $"Old text:{Environment.NewLine}{TrimPreview(oldString, 700)}{Environment.NewLine}{Environment.NewLine}" +
                $"New text:{Environment.NewLine}{TrimPreview(newString, 700)}{Environment.NewLine}{Environment.NewLine}";
        }

        if (string.Equals(toolName, "bash", StringComparison.Ordinal))
        {
            var command = GetString(input, "command");
            if (string.IsNullOrWhiteSpace(command))
            {
                return string.Empty;
            }

            return
                $"Command preview:{Environment.NewLine}" +
                $"{TrimPreview(command, 1000)}{Environment.NewLine}{Environment.NewLine}";
        }

        return string.Empty;
    }

    private static string GetString(IReadOnlyDictionary<string, object?> input, string key)
    {
        return input.TryGetValue(key, out var value) && value is string text ? text : string.Empty;
    }

    private static string Fallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string TrimPreview(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(empty)";
        }

        var normalized = value.ReplaceLineEndings(Environment.NewLine);
        return normalized.Length <= maxChars
            ? normalized
            : normalized[..maxChars] + Environment.NewLine + "...(truncated)";
    }
}

public sealed record DesktopPermissionEvent(
    string Outcome,
    string ToolName,
    PermissionRiskLevel RiskLevel,
    string Operation,
    string Target,
    PermissionApprovalChoice? Choice)
{
    public string DisplayText => DesktopPermissionEnforcer.FormatPermissionEvent(this);
}
