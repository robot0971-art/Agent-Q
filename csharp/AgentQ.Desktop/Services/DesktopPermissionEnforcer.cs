using System.Windows;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPermissionEnforcer(Window owner, AgentWorkMode workMode) : IPermissionEnforcer
{
    private readonly HashSet<PermissionRiskLevel> _approvedForRun = [];

    public event Action<IReadOnlyCollection<PermissionRiskLevel>>? ApprovedForRunChanged;

    public event Action<DesktopPermissionEvent>? PermissionEventRecorded;

    public IReadOnlyCollection<PermissionRiskLevel> ApprovedForRun => _approvedForRun.ToArray();

    public async Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson)
    {
        return await owner.Dispatcher.InvokeAsync(() =>
        {
            var policy = ToolPermissionPolicy.Evaluate(toolName, inputJson, workMode);
            var assessment = policy.Assessment;
            if (policy.IsBlocked)
            {
                RecordPermissionEvent("Blocked", toolName, assessment, PermissionApprovalChoice.Deny);
                System.Windows.MessageBox.Show(
                    owner,
                    $"Blocked by AgentQ safety policy.{Environment.NewLine}{Environment.NewLine}" +
                    $"Risk: {assessment.RiskLevel}{Environment.NewLine}" +
                    $"Operation: {assessment.Operation}{Environment.NewLine}" +
                    $"Target: {assessment.Target}{Environment.NewLine}" +
                    $"Mode: {workMode}{Environment.NewLine}" +
                    $"Policy: {policy.PolicyReason}{Environment.NewLine}{Environment.NewLine}" +
                    assessment.Reason,
                    "AgentQ permission blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            if (policy.Decision == ToolPermissionDecision.Allow)
            {
                RecordPermissionEvent("Allowed by policy", toolName, assessment, null);
                return true;
            }

            if (IsReusableApproval(assessment.RiskLevel) &&
                _approvedForRun.Contains(assessment.RiskLevel))
            {
                RecordPermissionEvent("Allowed by run approval", toolName, assessment, null);
                return true;
            }

            var preview = inputJson.Length > 1400
                ? inputJson[..1400] + Environment.NewLine + "...(truncated)"
                : inputJson;
            var focusedPreview = BuildFocusedPreview(toolName, inputJson);
            var approvalHint = IsReusableApproval(assessment.RiskLevel)
                ? $"{Environment.NewLine}Allow similar will skip repeat prompts for this operation type during the current run. Allow edits + builds will skip repeat prompts for workspace file edits and verification commands only."
                : string.Empty;
            var dialogContent = new PermissionDialogContent(
                BuildPermissionSummary(assessment),
                $"{assessment.RiskLevel} / {assessment.Operation}",
                assessment.Target,
                assessment.Reason + approvalHint,
                policy.PolicyReason,
                toolName,
                description,
                focusedPreview.Trim(),
                preview);

            var choice = PermissionApprovalDialog.Show(
                owner,
                $"AgentQ permission: {assessment.RiskLevel}",
                dialogContent,
                IsReusableApproval(assessment.RiskLevel),
                IsReusableApproval(assessment.RiskLevel));

            foreach (var approvedRisk in GetReusableApprovals(choice, assessment.RiskLevel))
            {
                _approvedForRun.Add(approvedRisk);
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
        ApprovedForRunChanged?.Invoke(ApprovedForRun);
    }

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
    {
        if (choice == PermissionApprovalChoice.AllowSimilarForRun &&
            IsReusableApproval(currentRiskLevel))
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

    public static string BuildPermissionSummary(ToolPermissionAssessment assessment)
    {
        return assessment.RiskLevel switch
        {
            PermissionRiskLevel.ProjectWrite => "AgentQ가 프로젝트 파일을 수정하려고 합니다.",
            PermissionRiskLevel.VerificationCommand => "AgentQ가 빌드 또는 테스트 명령을 실행하려고 합니다.",
            PermissionRiskLevel.GitWrite => "AgentQ가 Git 상태를 변경하려고 합니다.",
            PermissionRiskLevel.Network => "AgentQ가 네트워크를 사용할 수 있는 명령을 실행하려고 합니다.",
            PermissionRiskLevel.Destructive => "AgentQ가 위험한 작업으로 분류된 명령을 실행하려고 했습니다.",
            _ => "AgentQ가 승인 필요한 작업을 실행하려고 합니다."
        };
    }

    private static bool IsReusableApproval(PermissionRiskLevel riskLevel)
    {
        return riskLevel is PermissionRiskLevel.ProjectWrite or PermissionRiskLevel.VerificationCommand;
    }

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
