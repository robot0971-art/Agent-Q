namespace AgentQ.Desktop.Services;

public enum MultiAgentRole
{
    Planner,
    Coder,
    Reviewer,
    Tester
}

public sealed class MultiAgentRolePlan
{
    public required DesktopTaskKind Kind { get; init; }

    public required IReadOnlyList<MultiAgentRoleStep> Steps { get; init; }

    public string FormatForPrompt()
    {
        if (Steps.Count == 0)
        {
            return "Multi-agent role plan: no role split needed.";
        }

        var lines = Steps.Select(step =>
        {
            var mode = step.IsParallelCandidate ? "parallel candidate" : "local sequential";
            return $"- {step.Role}: {step.Responsibility} ({mode})";
        });

        return $"Multi-agent role plan:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }
}

public sealed class MultiAgentRoleStep
{
    public required MultiAgentRole Role { get; init; }

    public required string Responsibility { get; init; }

    public bool IsParallelCandidate { get; init; }
}

public static class MultiAgentRolePlanner
{
    public static MultiAgentRolePlan Build(DesktopTaskProfile profile)
    {
        IReadOnlyList<MultiAgentRoleStep> steps = profile.Kind switch
        {
            DesktopTaskKind.CodeReview =>
            new List<MultiAgentRoleStep>
            {
                Step(MultiAgentRole.Reviewer, "inspect changed files, regressions, security risks, and missing tests"),
                Step(MultiAgentRole.Tester, "identify verification gaps and the narrowest useful checks", parallel: true)
            },
            DesktopTaskKind.Analysis =>
            new List<MultiAgentRoleStep>
            {
                Step(MultiAgentRole.Planner, "map the question, evidence needs, and search order"),
                Step(MultiAgentRole.Reviewer, "separate confirmed facts from assumptions before final answer")
            },
            DesktopTaskKind.BugFix or DesktopTaskKind.VerificationFailure =>
            new List<MultiAgentRoleStep>
            {
                Step(MultiAgentRole.Planner, "classify the failure and choose the smallest repair surface"),
                Step(MultiAgentRole.Coder, "make the minimal root-cause change"),
                Step(MultiAgentRole.Tester, "rerun focused verification and summarize remaining risk")
            },
            DesktopTaskKind.Feature or DesktopTaskKind.Refactor =>
            new List<MultiAgentRoleStep>
            {
                Step(MultiAgentRole.Planner, "map existing patterns, contracts, and implementation slices"),
                Step(MultiAgentRole.Coder, "implement one cohesive slice without unrelated refactors"),
                Step(MultiAgentRole.Reviewer, "review touched behavior, compatibility, and edge cases", parallel: true),
                Step(MultiAgentRole.Tester, "select and run relevant build or test checks")
            },
            DesktopTaskKind.Documentation =>
            new List<MultiAgentRoleStep>
            {
                Step(MultiAgentRole.Planner, "identify source evidence before writing claims"),
                Step(MultiAgentRole.Reviewer, "check wording against inspected behavior and unsupported assumptions")
            },
            _ =>
            new List<MultiAgentRoleStep>
            {
                Step(MultiAgentRole.Planner, "decide whether a role split is useful for the request"),
                Step(MultiAgentRole.Reviewer, "check the final response for evidence and residual uncertainty")
            }
        };

        return new MultiAgentRolePlan
        {
            Kind = profile.Kind,
            Steps = steps
        };
    }

    private static MultiAgentRoleStep Step(MultiAgentRole role, string responsibility, bool parallel = false)
    {
        return new MultiAgentRoleStep
        {
            Role = role,
            Responsibility = responsibility,
            IsParallelCandidate = parallel
        };
    }
}
