namespace AgentQ.Desktop.Services;

public static class ToolPermissionPolicy
{
    public static ToolPermissionPolicyResult Evaluate(
        string toolName,
        string inputJson,
        AgentWorkMode workMode = AgentWorkMode.FullAgent)
    {
        return Evaluate(ToolPermissionClassifier.Assess(toolName, inputJson), workMode);
    }

    public static ToolPermissionPolicyResult Evaluate(
        string toolName,
        string inputJson,
        string workspaceRoot,
        AgentWorkMode workMode = AgentWorkMode.FullAgent)
    {
        return Evaluate(ToolPermissionClassifier.Assess(toolName, inputJson, workspaceRoot), workMode);
    }

    public static ToolPermissionPolicyResult Evaluate(
        ToolPermissionAssessment assessment,
        AgentWorkMode workMode = AgentWorkMode.FullAgent)
    {
        var target = assessment.Target.ToLowerInvariant();

        if (assessment.RiskLevel == PermissionRiskLevel.Destructive)
        {
            return Block(assessment, "Destructive commands are blocked by desktop policy.");
        }

        if (assessment.RiskLevel == PermissionRiskLevel.ExternalWrite)
        {
            return Block(assessment, "Writes outside the selected workspace are blocked by desktop policy.");
        }

        if (assessment.RiskLevel == PermissionRiskLevel.GitWrite &&
            (target.Contains("git push", StringComparison.Ordinal) ||
             target.Contains("git tag", StringComparison.Ordinal)))
        {
            return Block(assessment, "Remote or release Git write commands are blocked by desktop policy.");
        }

        if (assessment.RiskLevel == PermissionRiskLevel.SafeRead &&
            !IsReadOnlyShellOperation(assessment))
        {
            return Allow(assessment, "Safe read-only operation.");
        }

        // Full Agent is the user-facing "full workspace access" mode. Keep all
        // workspace/path policy checks above, but do not add a per-file dialog to
        // a deterministic, registry-bound scaffold creation.
        if (workMode == AgentWorkMode.FullAgent &&
            string.Equals(assessment.Operation, "Create project scaffold", StringComparison.OrdinalIgnoreCase) &&
            assessment.RiskLevel is PermissionRiskLevel.LowRiskProjectWrite or PermissionRiskLevel.ProjectWrite)
        {
            return Allow(assessment, "Full Agent mode permits a safe workspace-bound deterministic scaffold without an individual approval dialog.");
        }

        return workMode switch
        {
            AgentWorkMode.Readonly => Block(
                assessment,
                "Readonly mode blocks writes, shell commands, network access, and Git state changes."),
            AgentWorkMode.Coding => EvaluateCodingMode(assessment),
            _ => RequireApproval(assessment, "Full Agent mode requires explicit user approval for this operation.")
        };
    }

    private static ToolPermissionPolicyResult EvaluateCodingMode(ToolPermissionAssessment assessment)
    {
        return assessment.RiskLevel switch
        {
            PermissionRiskLevel.LowRiskProjectWrite => RequireApproval(
                assessment,
                "Coding mode requires explicit user approval before creating workspace files or folders."),
            PermissionRiskLevel.ProjectWrite => RequireApproval(
                assessment,
                "Coding mode allows workspace file edits with explicit user approval."),
            PermissionRiskLevel.VerificationCommand => RequireApproval(
                assessment,
                "Coding mode requires explicit user approval before running build or test shell commands."),
            PermissionRiskLevel.ShellCommand when IsLocalServerOperation(assessment) => RequireApproval(
                assessment,
                "Coding mode requires explicit user approval before starting or stopping a local development server."),
            PermissionRiskLevel.SafeRead when IsReadOnlyShellOperation(assessment) => RequireApproval(
                assessment,
                "Coding mode requires explicit user approval before running read-only shell commands."),
            PermissionRiskLevel.Network when string.Equals(assessment.Operation, "Web search", StringComparison.OrdinalIgnoreCase) => Allow(
                assessment,
                "Coding mode allows read-only public web search for evidence gathering."),
            _ => Block(
                assessment,
                "Coding mode blocks broad shell, network, and Git write operations. Switch to Full Agent mode if this is intended.")
        };
    }

    private static bool IsLocalServerOperation(ToolPermissionAssessment assessment)
    {
        return string.Equals(assessment.Operation, "Start local development server", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(assessment.Operation, "Stop local development server", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReadOnlyShellOperation(ToolPermissionAssessment assessment) =>
        string.Equals(assessment.Operation, "Read-only shell command", StringComparison.OrdinalIgnoreCase);

    private static ToolPermissionPolicyResult Allow(ToolPermissionAssessment assessment, string reason)
    {
        return new ToolPermissionPolicyResult
        {
            Assessment = assessment,
            Decision = ToolPermissionDecision.Allow,
            PolicyReason = reason
        };
    }

    private static ToolPermissionPolicyResult RequireApproval(ToolPermissionAssessment assessment, string reason)
    {
        return new ToolPermissionPolicyResult
        {
            Assessment = assessment,
            Decision = ToolPermissionDecision.RequireApproval,
            PolicyReason = reason
        };
    }

    private static ToolPermissionPolicyResult Block(ToolPermissionAssessment assessment, string reason)
    {
        return new ToolPermissionPolicyResult
        {
            Assessment = assessment,
            Decision = ToolPermissionDecision.Block,
            PolicyReason = reason
        };
    }
}
