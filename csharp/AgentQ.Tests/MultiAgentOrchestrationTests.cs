using System.Collections.Generic;
using System.Linq;
using AgentQ.Desktop.Services;
using Xunit;

namespace AgentQ.Tests;

public sealed class MultiAgentOrchestrationTests
{
    [Fact]
    public void AgentRoleCatalog_ShouldProvideCorrectRolesAndTools()
    {
        var planner = AgentRoleCatalog.Planner;
        Assert.Equal(MultiAgentRole.Planner, planner.Role);
        Assert.Contains("read_file", planner.AllowedTools);
        Assert.DoesNotContain("write_file", planner.AllowedTools);

        var coder = AgentRoleCatalog.Coder;
        Assert.Equal(MultiAgentRole.Coder, coder.Role);
        Assert.Contains("write_file", coder.AllowedTools);
        Assert.Contains("edit_file", coder.AllowedTools);

        var reviewer = AgentRoleCatalog.Reviewer;
        Assert.Equal(MultiAgentRole.Reviewer, reviewer.Role);
        Assert.Contains("read_file", reviewer.AllowedTools);
        Assert.DoesNotContain("write_file", reviewer.AllowedTools);
    }

    [Fact]
    public void MultiAgentRolePlan_ShouldResolveCorrectRoles()
    {
        var steps = new List<MultiAgentRoleStep>
        {
            new() { Role = MultiAgentRole.Planner, Responsibility = "Plan task" },
            new() { Role = MultiAgentRole.Coder, Responsibility = "Code task" }
        };

        var plan = new MultiAgentRolePlan
        {
            Kind = DesktopTaskKind.Feature,
            Steps = steps
        };

        var resolved = plan.ResolveRoles();
        Assert.Equal(2, resolved.Count);
        Assert.Equal(MultiAgentRole.Planner, resolved[0].Role);
        Assert.Equal(MultiAgentRole.Coder, resolved[1].Role);
    }
}
