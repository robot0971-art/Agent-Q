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

        if (assessment.RiskLevel == PermissionRiskLevel.SafeRead)
        {
            return Allow(assessment, "Safe read-only operation.");
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
            PermissionRiskLevel.ProjectWrite => RequireApproval(
                assessment,
                "Coding mode allows workspace file edits with explicit user approval."),
            PermissionRiskLevel.VerificationCommand => RequireApproval(
                assessment,
                "Coding mode allows build and test commands with explicit user approval."),
            _ => Block(
                assessment,
                "Coding mode blocks broad shell, network, and Git write operations. Switch to Full Agent mode if this is intended.")
        };
    }

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
