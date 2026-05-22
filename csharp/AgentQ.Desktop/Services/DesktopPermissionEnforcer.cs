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
            var focusedPreview = BuildFocusedPreview(toolName, inputJson);
            var approvalHint = IsReusableApproval(assessment.RiskLevel)
                ? $"{Environment.NewLine}Allow all for this run will skip repeat prompts for workspace file edits and verification commands only."
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
                focusedPreview +
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
