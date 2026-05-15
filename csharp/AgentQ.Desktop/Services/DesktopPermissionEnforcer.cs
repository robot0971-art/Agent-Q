using System.Windows;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPermissionEnforcer(Window owner, AgentWorkMode workMode) : IPermissionEnforcer
{
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

            var preview = inputJson.Length > 1400
                ? inputJson[..1400] + Environment.NewLine + "...(truncated)"
                : inputJson;

            var message =
                $"Allow AgentQ to run this operation?{Environment.NewLine}{Environment.NewLine}" +
                $"Risk: {assessment.RiskLevel}{Environment.NewLine}" +
                $"Operation: {assessment.Operation}{Environment.NewLine}" +
                $"Target: {assessment.Target}{Environment.NewLine}" +
                $"Mode: {workMode}{Environment.NewLine}" +
                $"Reason: {assessment.Reason}{Environment.NewLine}{Environment.NewLine}" +
                $"Policy: {policy.PolicyReason}{Environment.NewLine}{Environment.NewLine}" +
                $"Tool: {toolName}{Environment.NewLine}" +
                $"Description: {description}{Environment.NewLine}{Environment.NewLine}" +
                $"Input:{Environment.NewLine}{preview}";

            return System.Windows.MessageBox.Show(
                owner,
                message,
                $"AgentQ permission: {assessment.RiskLevel}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        });
    }
}
