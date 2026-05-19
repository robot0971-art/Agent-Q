using System.Windows;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPermissionEnforcer(Window owner, AgentWorkMode workMode) : IPermissionEnforcer
{
    private readonly HashSet<PermissionRiskLevel> _approvedForRun = [];

    public async Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson)
    {
        return await owner.Dispatcher.InvokeAsync(() =>
        {
            var policy = ToolPermissionPolicy.Evaluate(toolName, inputJson, workMode);
            var assessment = policy.Assessment;
            if (policy.IsBlocked)
            {
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
                return true;
            }

            if (IsReusableApproval(assessment.RiskLevel) &&
                _approvedForRun.Contains(assessment.RiskLevel))
            {
                return true;
            }

            var preview = inputJson.Length > 1400
                ? inputJson[..1400] + Environment.NewLine + "...(truncated)"
                : inputJson;

            var approvalHint = IsReusableApproval(assessment.RiskLevel)
                ? $"{Environment.NewLine}전체 권한 허용: 이번 실행 동안 프로젝트 파일 쓰기와 검증 명령을 다시 묻지 않습니다."
                : string.Empty;

            var message =
                $"Allow AgentQ to run this operation?{Environment.NewLine}{Environment.NewLine}" +
                $"Risk: {assessment.RiskLevel}{Environment.NewLine}" +
                $"Operation: {assessment.Operation}{Environment.NewLine}" +
                $"Target: {assessment.Target}{Environment.NewLine}" +
                $"Mode: {workMode}{Environment.NewLine}" +
                $"Reason: {assessment.Reason}{approvalHint}{Environment.NewLine}{Environment.NewLine}" +
                $"Policy: {policy.PolicyReason}{Environment.NewLine}{Environment.NewLine}" +
                $"Tool: {toolName}{Environment.NewLine}" +
                $"Description: {description}{Environment.NewLine}{Environment.NewLine}" +
                $"Input:{Environment.NewLine}{preview}";

            var choice = PermissionApprovalDialog.Show(
                owner,
                $"AgentQ permission: {assessment.RiskLevel}",
                message,
                IsReusableApproval(assessment.RiskLevel));

            if (choice == PermissionApprovalChoice.AllowAllForRun)
            {
                _approvedForRun.Add(PermissionRiskLevel.ProjectWrite);
                _approvedForRun.Add(PermissionRiskLevel.VerificationCommand);
            }

            return choice is PermissionApprovalChoice.AllowOnce or PermissionApprovalChoice.AllowAllForRun;
        });
    }

    private static bool IsReusableApproval(PermissionRiskLevel riskLevel)
    {
        return riskLevel is PermissionRiskLevel.ProjectWrite or PermissionRiskLevel.VerificationCommand;
    }
}
