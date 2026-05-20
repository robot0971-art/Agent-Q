using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;
using Xunit;

namespace AgentQ.Tests;

public sealed class DesktopServiceTests
{
    [Theory]
    [InlineData(AgentWorkMode.Readonly, 20)]
    [InlineData(AgentWorkMode.Coding, 50)]
    [InlineData(AgentWorkMode.FullAgent, 50)]
    public void MainViewModel_ToConfiguration_SetsDesktopToolStepBudget(AgentWorkMode workMode, int expectedMaxToolSteps)
    {
        var viewModel = new MainViewModel
        {
            WorkMode = workMode
        };

        var config = viewModel.ToConfiguration();

        Assert.Equal(expectedMaxToolSteps, config.DesktopMaxToolSteps);
    }

    [Fact]
    public void DesktopProviderModelCatalog_ProvidesDefaultsForKnownAndUnknownProviders()
    {
        Assert.Contains("opencode-go", DesktopProviderModelCatalog.Providers);
        Assert.Equal("kimi-k2.6", DesktopProviderModelCatalog.GetDefaultModel("opencode-go"));
        Assert.Equal("https://api.openai.com/v1", DesktopProviderModelCatalog.GetDefaultBaseUrl("openai", string.Empty));
        Assert.Equal("default", DesktopProviderModelCatalog.GetDefaultModel("custom-provider"));
        Assert.Equal("https://example.test", DesktopProviderModelCatalog.GetDefaultBaseUrl("custom-provider", "https://example.test"));
    }

    [Theory]
    [InlineData("## main...origin/main [ahead 2, behind 3]", "Diverged from origin/main")]
    [InlineData("## feature...origin/feature [gone]", "Upstream origin/feature is gone")]
    [InlineData("## local-only", "No upstream is configured")]
    [InlineData("## main...origin/main [behind 4]", "Pull is likely needed")]
    public void GitBranchStatusAnalyzer_ExplainsRiskyBranchStates(string statusOutput, string expected)
    {
        var summary = GitBranchStatusAnalyzer.Analyze(statusOutput);

        Assert.Contains(expected, summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("M ", true, false)]
    [InlineData(" M", false, true)]
    [InlineData("MM", true, true)]
    [InlineData("??", false, true)]
    public void GitChangedFile_ReportsStagedAndUnstagedState(string status, bool expectedStaged, bool expectedUnstaged)
    {
        var file = new GitChangedFile
        {
            Status = status,
            Path = "README.md"
        };

        Assert.Equal(expectedStaged, file.IsStaged);
        Assert.Equal(expectedUnstaged, file.IsUnstaged);
    }

    [Theory]
    [InlineData("## main...origin/main [behind 2]", true, "Behind by 2")]
    [InlineData("## main...origin/main", true, "Safe to check")]
    [InlineData("## main...origin/main [ahead 1]", false, "local commits")]
    [InlineData("## main...origin/main [ahead 1, behind 2]", false, "diverged")]
    [InlineData("## feature...origin/feature [gone]", false, "upstream branch is gone")]
    [InlineData("## local-only", false, "No upstream")]
    public void GitPullSafetyAnalyzer_AllowsOnlySafeFastForwardPulls(string statusOutput, bool expectedCanPull, string expectedReason)
    {
        var analysis = GitPullSafetyAnalyzer.Analyze(statusOutput, []);

        Assert.Equal(expectedCanPull, analysis.CanPull);
        Assert.Contains(expectedReason, analysis.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitPullSafetyAnalyzer_BlocksDirtyWorkingTrees()
    {
        var analysis = GitPullSafetyAnalyzer.Analyze(
            "## main...origin/main [behind 1]",
            [
                new GitChangedFile
                {
                    Status = " M",
                    Path = "README.md"
                }
            ]);

        Assert.False(analysis.CanPull);
        Assert.Contains("local changes", analysis.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitBranchRecoveryAnalyzer_CreatesTimestampedBackupBranchName()
    {
        var branchName = GitBranchRecoveryAnalyzer.CreateBackupBranchName(new DateTime(2026, 5, 20, 10, 30, 45));

        Assert.Equal("backup/desktop-recovery-20260520-103045", branchName);
    }

    [Fact]
    public void GitBranchRecoveryAnalyzer_BlocksBranchSwitchWithLocalChanges()
    {
        var canSwitch = GitBranchRecoveryAnalyzer.CanSwitchBranch(
            [
                new GitChangedFile
                {
                    Status = " M",
                    Path = "README.md"
                }
            ],
            out var reason);

        Assert.False(canSwitch);
        Assert.Contains("local changes", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitBranchRecoveryAnalyzer_AllowsBranchSwitchWithCleanTree()
    {
        var canSwitch = GitBranchRecoveryAnalyzer.CanSwitchBranch([], out var reason);

        Assert.True(canSwitch);
        Assert.Contains("clean", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopPlanParser_ParsesCheckboxNumberedAndBulletItems()
    {
        var items = DesktopPlanParser.Parse(
            """
            # Plan
            - [x] Fix config isolation.
            - [-] Restore Korean strings.
            3. Add desktop service tests.
            * Update docs.
            - [!] Resolve blocker.
            """);

        Assert.Equal(5, items.Count);
        Assert.Equal(AgentPlanItemStatus.Done, items[0].Status);
        Assert.Equal("Fix config isolation", items[0].Title);
        Assert.Equal(AgentPlanItemStatus.InProgress, items[1].Status);
        Assert.Equal(AgentPlanItemStatus.Pending, items[2].Status);
        Assert.Equal("Add desktop service tests", items[2].Title);
        Assert.Equal(AgentPlanItemStatus.Blocked, items[4].Status);
    }

    [Fact]
    public void ToolPermissionPolicy_ReadonlyBlocksProjectWrites()
    {
        var assessment = new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.ProjectWrite,
            Operation = "Write file",
            Target = "README.md",
            Reason = "This will modify a file inside the selected workspace."
        };

        var result = ToolPermissionPolicy.Evaluate(assessment, AgentWorkMode.Readonly);

        Assert.Equal(ToolPermissionDecision.Block, result.Decision);
        Assert.True(result.IsBlocked);
        Assert.Contains("Readonly mode", result.PolicyReason);
    }

    [Fact]
    public void ToolPermissionPolicy_CodingRequiresApprovalForVerificationCommands()
    {
        var assessment = ToolPermissionClassifier.Assess(
            "bash",
            new Dictionary<string, object?>
            {
                ["command"] = "dotnet test csharp\\AgentQ.sln"
            });

        var result = ToolPermissionPolicy.Evaluate(assessment, AgentWorkMode.Coding);

        Assert.Equal(PermissionRiskLevel.VerificationCommand, assessment.RiskLevel);
        Assert.Equal(ToolPermissionDecision.RequireApproval, result.Decision);
        Assert.False(result.IsBlocked);
    }

    [Fact]
    public void ToolPermissionPolicy_BlocksDestructiveCommandsInFullAgentMode()
    {
        var assessment = ToolPermissionClassifier.Assess(
            "bash",
            new Dictionary<string, object?>
            {
                ["command"] = "git reset --hard"
            });

        var result = ToolPermissionPolicy.Evaluate(assessment, AgentWorkMode.FullAgent);

        Assert.Equal(PermissionRiskLevel.Destructive, assessment.RiskLevel);
        Assert.Equal(ToolPermissionDecision.Block, result.Decision);
    }

    [Fact]
    public void VerificationFailureClassifier_DetectsCompilerErrors()
    {
        var classifier = new VerificationFailureClassifier();
        var analysis = classifier.Analyze(
            new AgentVerificationPlan
            {
                Title = "Build",
                Command = "dotnet build"
            },
            new VerificationRunResult
            {
                ExitCode = 1,
                StandardError = "Program.cs(10,5): error CS1002: ; expected"
            });

        Assert.Equal(VerificationFailureKind.CompileError, analysis.Kind);
        Assert.Equal("Compilation failed", analysis.Title);
        Assert.Contains(analysis.Evidence, line => line.Contains("CS1002", StringComparison.Ordinal));
    }

    [Fact]
    public void VerificationFailureClassifier_DetectsMissingDependency()
    {
        var classifier = new VerificationFailureClassifier();
        var analysis = classifier.Analyze(
            new AgentVerificationPlan
            {
                Title = "Custom verification",
                Command = "my-missing-command"
            },
            new VerificationRunResult
            {
                ExitCode = 1,
                StandardError = "my-missing-command: command not found"
            });

        Assert.Equal(VerificationFailureKind.MissingDependency, analysis.Kind);
        Assert.Contains("Missing command", analysis.Title, StringComparison.OrdinalIgnoreCase);
    }
}
