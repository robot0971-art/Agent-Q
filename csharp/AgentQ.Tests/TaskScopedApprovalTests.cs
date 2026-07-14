using AgentQ.Desktop.Services;
using Xunit;

namespace AgentQ.Tests;

public sealed class TaskScopedApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ThisTask_ApprovalMatchesOnlyTheBoundTaskRunWorkspaceAndCapability()
    {
        var store = new TaskScopedApprovalStore();
        store.Grant(new TaskScopedApprovalRequest("task-1", "run-1", ".", PermissionRiskLevel.ProjectWrite, UserApprovalMode.ThisTask, Now.AddMinutes(10)), Now);

        Assert.True(store.IsApproved("task-1", "run-1", ".", PermissionRiskLevel.ProjectWrite, Now));
        Assert.False(store.IsApproved("task-2", "run-1", ".", PermissionRiskLevel.ProjectWrite, Now));
        Assert.False(store.IsApproved("task-1", "run-2", ".", PermissionRiskLevel.ProjectWrite, Now));
        Assert.False(store.IsApproved("task-1", "run-1", ".", PermissionRiskLevel.VerificationCommand, Now));
    }

    [Fact]
    public void FullAccess_IsStillBoundAndDoesNotGrantBlockedCapabilities()
    {
        var store = new TaskScopedApprovalStore();
        store.Grant(new TaskScopedApprovalRequest("task-1", "run-1", ".", PermissionRiskLevel.ProjectWrite, UserApprovalMode.FullAccess, Now.AddMinutes(10)), Now);

        Assert.True(store.IsApproved("task-1", "run-1", ".", PermissionRiskLevel.GitWrite, Now));
        Assert.False(store.IsApproved("task-1", "run-2", ".", PermissionRiskLevel.GitWrite, Now));
        Assert.False(store.IsApproved("task-1", "run-1", Path.GetTempPath(), PermissionRiskLevel.GitWrite, Now));
        Assert.False(store.IsApproved("task-1", "run-1", ".", PermissionRiskLevel.ExternalWrite, Now));
        Assert.False(store.IsApproved("task-1", "run-1", ".", PermissionRiskLevel.Destructive, Now));
    }

    [Fact]
    public void FullAccess_IdentityLookupRemainsTaskRunAndWorkspaceBound()
    {
        var store = new TaskScopedApprovalStore();
        store.Grant(new TaskScopedApprovalRequest("task-1", "run-1", ".", PermissionRiskLevel.ProjectWrite, UserApprovalMode.FullAccess, Now.AddMinutes(10)), Now);

        Assert.True(store.HasFullAccess("task-1", "run-1", ".", Now));
        Assert.False(store.HasFullAccess("task-2", "run-1", ".", Now));
        Assert.False(store.HasFullAccess("task-1", "run-2", ".", Now));
        Assert.False(store.HasFullAccess("task-1", "run-1", Path.GetTempPath(), Now));
    }

    [Fact]
    public void Approval_ExpiresAndCanBeRevoked()
    {
        var store = new TaskScopedApprovalStore();
        var approval = store.Grant(new TaskScopedApprovalRequest("task-1", "run-1", ".", PermissionRiskLevel.ProjectWrite, UserApprovalMode.ThisTask, Now.AddMinutes(1)), Now);
        Assert.True(store.Revoke(approval.ApprovalId));
        Assert.False(store.IsApproved("task-1", "run-1", ".", PermissionRiskLevel.ProjectWrite, Now));

        store.Grant(new TaskScopedApprovalRequest("task-1", "run-1", ".", PermissionRiskLevel.ProjectWrite, UserApprovalMode.ThisTask, Now.AddMinutes(1)), Now);
        Assert.False(store.IsApproved("task-1", "run-1", ".", PermissionRiskLevel.ProjectWrite, Now.AddMinutes(1)));
    }

    [Theory]
    [InlineData(PermissionRiskLevel.Destructive)]
    [InlineData(PermissionRiskLevel.ExternalWrite)]
    public void BlockedCapabilities_CannotBeGranted(PermissionRiskLevel capability)
    {
        var store = new TaskScopedApprovalStore();
        Assert.Throws<ArgumentException>(() => store.Grant(new TaskScopedApprovalRequest("task-1", "run-1", ".", capability, UserApprovalMode.FullAccess, Now.AddMinutes(1)), Now));
    }

    [Fact]
    public void Labels_ExposeTheThreeUserFacingModes()
    {
        Assert.Equal("\uAD8C\uD55C \uC2B9\uC778", UserApprovalModeLabels.DisplayName(UserApprovalMode.PerAction));
        Assert.Equal("\uC791\uC5C5 \uC804\uCCB4 \uC2B9\uC778", UserApprovalModeLabels.DisplayName(UserApprovalMode.ThisTask));
        Assert.Equal("\uC804\uCCB4 \uC561\uC138\uC2A4", UserApprovalModeLabels.DisplayName(UserApprovalMode.FullAccess));
    }
}
