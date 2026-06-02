using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Tools;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace AgentQ.Tests;

public sealed class DesktopServiceTests
{
    [Fact]
    public void ShellVerificationResultDetector_CreatesCardForPassedDotnetTest()
    {
        var content = JsonSerializer.Serialize(new
        {
            exitCode = 0,
            stdout = "Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2",
            stderr = "",
            stdoutTruncated = false,
            stderrTruncated = false,
            timeoutMs = 120000
        });

        var created = ShellVerificationResultDetector.TryCreate(
            "bash",
            new Dictionary<string, object?> { ["command"] = "dotnet test --filter ParserTests" },
            content,
            out var result);

        Assert.True(created);
        Assert.Equal("PASSED", result.Status);
        Assert.Equal("dotnet test", result.Title);
        Assert.Contains("dotnet test", result.Command);
        Assert.Contains("Shell verification passed", result.Summary);
    }

    [Fact]
    public void ShellVerificationResultDetector_CreatesCardForLocalizedPassedDotnetTest()
    {
        var content = JsonSerializer.Serialize(new
        {
            exitCode = 0,
            stdout = "\uD1B5\uACFC!  - \uC2E4\uD328:     0, \uD1B5\uACFC:     2, \uAC74\uB108\uB700:     0, \uC804\uCCB4:     2",
            stderr = "",
            stdoutTruncated = false,
            stderrTruncated = false,
            timeoutMs = 120000
        });

        var created = ShellVerificationResultDetector.TryCreate(
            "bash",
            new Dictionary<string, object?> { ["command"] = "dotnet test --filter ParserTests" },
            content,
            out var result);

        Assert.True(created);
        Assert.Equal("PASSED", result.Status);
        Assert.Equal("dotnet test", result.Title);
    }

    [Fact]
    public void ShellVerificationResultDetector_CreatesCardForLocalizedPassedDotnetBuild()
    {
        var content = JsonSerializer.Serialize(new
        {
            exitCode = 0,
            stdout = "\uBE4C\uB4DC\uD588\uC2B5\uB2C8\uB2E4.\r\n    \uACBD\uACE0 0\uAC1C\r\n    \uC624\uB958 0\uAC1C",
            stderr = "",
            stdoutTruncated = false,
            stderrTruncated = false,
            timeoutMs = 120000
        });

        var created = ShellVerificationResultDetector.TryCreate(
            "bash",
            new Dictionary<string, object?> { ["command"] = "dotnet build .\\Game.csproj --no-restore" },
            content,
            out var result);

        Assert.True(created);
        Assert.Equal("PASSED", result.Status);
        Assert.Equal("dotnet build", result.Title);
        Assert.Contains("dotnet build", result.Command);
    }

    [Fact]
    public void ShellVerificationResultDetector_IgnoresFailedVerificationCommand()
    {
        var content = JsonSerializer.Serialize(new
        {
            exitCode = 1,
            stdout = "Failed!  - Failed:     1, Passed:     1",
            stderr = "",
            stdoutTruncated = false,
            stderrTruncated = false,
            timeoutMs = 120000
        });

        var created = ShellVerificationResultDetector.TryCreate(
            "bash",
            new Dictionary<string, object?> { ["command"] = "dotnet test" },
            content,
            out _);

        Assert.False(created);
    }

    [Fact]
    public void ShellVerificationResultDetector_CreatesCardForPassedViteBuild()
    {
        var content = JsonSerializer.Serialize(new
        {
            exitCode = 0,
            stdout = "vite v8.0.14 building client environment for production...\n\u2713 built in 234ms",
            stderr = "",
            stdoutTruncated = false,
            stderrTruncated = false,
            timeoutMs = 120000
        });

        var created = ShellVerificationResultDetector.TryCreate(
            "bash",
            new Dictionary<string, object?> { ["command"] = "npm run build" },
            content,
            out var result);

        Assert.True(created);
        Assert.Equal("PASSED", result.Status);
        Assert.Equal("frontend build", result.Title);
    }

    [Fact]
    public void ShellVerificationResultDetector_IgnoresNonVerificationCommand()
    {
        var content = JsonSerializer.Serialize(new
        {
            exitCode = 0,
            stdout = "Passed!",
            stderr = "",
            stdoutTruncated = false,
            stderrTruncated = false,
            timeoutMs = 120000
        });

        var created = ShellVerificationResultDetector.TryCreate(
            "bash",
            new Dictionary<string, object?> { ["command"] = "git status --short" },
            content,
            out _);

        Assert.False(created);
    }

    [Fact]
    public void MainWindow_DoesNotExposeStaticErrorLegendInStatusPanel()
    {
        var xaml = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "MainWindow.xaml"));

        Assert.DoesNotContain("Text=\"ERROR\"", xaml);
    }

    [Fact]
    public void MainWindow_ExposesRunPermissionStatusAndResetAction()
    {
        var xaml = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "MainWindow.xaml"));
        var codeBehind = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "MainWindow.xaml.cs"));

        Assert.Contains("RunPermissionStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("CanClearRunPermissions", xaml, StringComparison.Ordinal);
        Assert.Contains("ResetRunPermissions_OnClick", xaml, StringComparison.Ordinal);
        Assert.Contains("ClearRunPermissions", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void RunTimelinePanel_WrapsLongEvidenceTextWithoutHorizontalScroll()
    {
        var xaml = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "Views", "RunTimelinePanel.xaml"));

        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"WrapWithOverflow\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopAgentService_SystemPrompt_PrioritizesSymbolSearchForCodeNavigation()
    {
        var field = typeof(DesktopAgentService).GetField("SystemPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        var prompt = Assert.IsType<string>(field?.GetValue(null));

        Assert.Contains("prefer symbol_search first", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hybrid_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("semantic_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grep_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list_directory", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirmed facts", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supporting files", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("can attempt to read HTTP/HTTPS links", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot access external websites", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("patch-sized edits", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SerializeField", prompt, StringComparison.Ordinal);
        Assert.Contains("destructive restore", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoFile(params string[] pathSegments)
    {
        var directory = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (directory is not null)
        {
            var parts = new string[pathSegments.Length + 1];
            parts[0] = directory.FullName;
            System.Array.Copy(pathSegments, 0, parts, 1, pathSegments.Length);
            var candidate = System.IO.Path.Combine(parts);
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new System.IO.FileNotFoundException("Could not find repository file.", string.Join("/", pathSegments));
    }

    [Fact]
    public void DesktopModelRoutingAdvisor_RecommendsSmallFastForReadonlyAnalysis()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("README check and analyze");
        var recommendation = DesktopModelRoutingAdvisor.Recommend(
            "README check and analyze",
            profile,
            new ProviderConfiguration
            {
                Provider = "openai",
                Model = "gpt-5.4-mini"
            },
            AgentWorkMode.Readonly);

        Assert.Equal(DesktopModelRoutingTier.SmallFast, recommendation.Tier);
        Assert.Contains("mini", recommendation.SuggestedModel, StringComparison.OrdinalIgnoreCase);
        Assert.True(recommendation.CurrentModelMatches);
    }

    [Fact]
    public void DesktopModelRoutingAdvisor_RecommendsLargeFrontierForComplexRefactor()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("architecture refactor for the whole project");
        var recommendation = DesktopModelRoutingAdvisor.Recommend(
            "architecture refactor for the whole project",
            profile,
            new ProviderConfiguration
            {
                Provider = "anthropic",
                Model = "claude-haiku-4-5"
            },
            AgentWorkMode.Coding);

        Assert.Equal(DesktopModelRoutingTier.LargeFrontier, recommendation.Tier);
        Assert.Contains("opus", recommendation.SuggestedModel, StringComparison.OrdinalIgnoreCase);
        Assert.False(recommendation.CurrentModelMatches);
    }

    [Theory]
    [InlineData("\uCEF4\uD30C\uC77C \uC624\uB958 \uACE0\uCCD0\uC918", DesktopTaskKind.VerificationFailure)]
    [InlineData("\uC0C8 \uAE30\uB2A5 \uCD94\uAC00\uD574\uC918", DesktopTaskKind.Feature)]
    [InlineData("\uC774 \uBCC0\uACBD\uC0AC\uD56D \uCF54\uB4DC \uB9AC\uBDF0\uD574\uC918", DesktopTaskKind.CodeReview)]
    [InlineData("README \uBB38\uC11C \uACE0\uCCD0\uC918", DesktopTaskKind.Documentation)]
    [InlineData("\uAD6C\uC870\uB97C \uBD84\uC11D\uD574\uC918", DesktopTaskKind.Analysis)]
    [InlineData("\uC6F9\uC5D0\uC11C \uAC80\uC0C9\uD574\uC918", DesktopTaskKind.Analysis)]
    [InlineData("\uD504\uB85C\uC81D\uD2B8 \uAD6C\uC870 \uB9AC\uD329\uD130\uB9C1\uD574\uC918", DesktopTaskKind.Refactor)]
    [InlineData("Build a portfolio website", DesktopTaskKind.Feature)]
    [InlineData("Create a landing page", DesktopTaskKind.Feature)]
    [InlineData("\uD30C\uC774\uC36C\uC73C\uB85C \uAC04\uB2E8\uD55C \uB370\uC774\uD130 \uBD84\uC11D \uB3C4\uAD6C\uB97C \uB9CC\uB4E4\uC5B4 \uBCF4\uC790", DesktopTaskKind.Feature)]
    [InlineData("\uAC1C\uBC1C\uC790 \uAE30\uBCF8 \uB2E8\uC5B4\uC7A5 \uC6F9", DesktopTaskKind.Feature)]
    public void DesktopTaskClassifier_ClassifiesCommonTaskTypes(string text, DesktopTaskKind expected)
    {
        Assert.Equal(expected, DesktopTaskClassifier.Classify(text));
    }

    [Fact]
    public void DesktopPromptAssemblyService_AddsTaskSpecificGuidance()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("\uCEF4\uD30C\uC77C \uC624\uB958 \uACE0\uCCD0\uC918");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base prompt", profile);

        Assert.Equal(DesktopTaskKind.VerificationFailure, profile.Kind);
        Assert.Contains("Dynamic task guidance", prompt);
        Assert.Contains("Context prioritization", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(profile.ContextHint, prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tool routing rules", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list_directory", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not use bash just to list files", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("symbol_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Execution strategy", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verification failure", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Treat compiler, linter, and test output", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("narrowest useful verification command", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopPromptAssemblyService_IncludesToolPermissionState()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("fix compile error");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt(
            "Base prompt",
            profile,
            "Tool Permission State:\n- allowed: read_file\n- requires approval: edit_file");

        Assert.Contains("Tool Permission State:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allowed: read_file", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requires approval: edit_file", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopPromptAssemblyService_AddsScaffoldDecisionRulesForFeatureTasks()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("\uD30C\uC774\uC36C\uC73C\uB85C \uAC04\uB2E8\uD55C \uB370\uC774\uD130 \uBD84\uC11D \uB3C4\uAD6C\uB97C \uB9CC\uB4E4\uC5B4 \uBCF4\uC790");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base prompt", profile);

        Assert.Contains("Scaffold decision rules", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("optional accelerators", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plan_project_scaffold", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workspace tools", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bare request for a 'new project'", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before picking React", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopPromptAssemblyService_AddsFinalReportingRules()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("fix compile error");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base prompt", profile);

        Assert.Contains("Final response rules", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("root cause", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("changed files", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verification command", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not include long code blocks or diff blocks", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("copy and paste", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requested scope", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("optional follow-up findings", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("minimal root cause", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopPromptAssemblyService_AddsUserIntentPrecedenceRules()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("make the portfolio in JavaScript instead");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base prompt", profile);

        Assert.Contains("User intent precedence", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("latest explicit instruction overrides", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JavaScript files and commands", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not continue with TypeScript", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopAgentService_BuildsExplicitJavaScriptStackOverride()
    {
        var context = DesktopAgentService.BuildExplicitStackPreferenceContext("\uC790\uBC14\uC2A4\uD06C\uB9BD\uD2B8\uB85C \uBD80\uD0C1");

        Assert.Contains("JavaScript was explicitly requested", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".js/.jsx", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not choose TypeScript", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopAgentService_RetriesManualFallbackBeforeWorkspaceActions()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryManualFallback(
            "\uC544\uB798\uCC98\uB7FC \uC218\uC815\uD558\uC138\uC694.\n```csharp\nDeliberateAgentQCompileBreak();\n```",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_PreflightsBareNewProjectClarification()
    {
        var shouldClarify = DesktopAgentService.TryBuildPreflightClarification(
            "\uC5EC\uAE30\uC5D0 \uC0C8\uB85C\uC6B4 \uD504\uB85C\uC81D\uD2B8\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature,
            out var message);

        Assert.True(shouldClarify);
        Assert.Contains("\uC5B4\uB5A4 \uC885\uB958\uC758 \uD504\uB85C\uC81D\uD2B8", message, StringComparison.Ordinal);
        Assert.Contains("Python", message, StringComparison.Ordinal);
        Assert.Contains("\uD3EC\uD2B8\uD3F4\uB9AC\uC624", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopAgentService_DoesNotPreflightConcreteProjectDirection()
    {
        var shouldClarify = DesktopAgentService.TryBuildPreflightClarification(
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uD648\uD398\uC774\uC9C0\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature,
            out var message);

        Assert.False(shouldClarify);
        Assert.Equal(string.Empty, message);
    }

    [Fact]
    public void DesktopAgentService_RetriesGenericGreetingAfterCodingTask()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryGenericGreetingFallback(
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uD648\uD398\uC774\uC9C0 \uC790\uBC14\uC2A4\uD06C\uB9BD\uD2B8\uB85C \uB9CC\uB4E4\uC5B4\uC918",
            "\uC548\uB155\uD558\uC138\uC694! \uBB34\uC5C7\uC744 \uB3C4\uC640\uB4DC\uB9B4\uAE4C\uC694?",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_RetriesBroadClarificationAfterCodingTask()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryGenericGreetingFallback(
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uD648\uD398\uC774\uC9C0 \uC790\uBC14\uC2A4\uD06C\uB9BD\uD2B8\uB85C \uB9CC\uB4E4\uC5B4\uC918",
            "\uD604\uC7AC \uD504\uB85C\uC81D\uD2B8\uB294 Next.js 14 + TypeScript\uB85C \uBCF4\uC785\uB2C8\uB2E4. \uAD6C\uCCB4\uC801\uC73C\uB85C \uC5B4\uB5A4 \uAE30\uB2A5\uC744 \uC6D0\uD558\uC2DC\uB294\uC9C0 \uC54C\uB824\uC8FC\uC2DC\uBA74 JavaScript(.js/.jsx)\uB85C \uAD6C\uD604\uD558\uACA0\uC2B5\uB2C8\uB2E4.",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_RetriesNoRequestClaimAfterCodingTask()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryGenericGreetingFallback(
            "\uC1FC\uD551\uBAB0 \uC7A5\uBC14\uAD6C\uB2C8 \uAE30\uB2A5 \uC790\uBC14\uC2A4\uD06C\uB9BD\uD2B8\uB85C \uC218\uC815\uD574\uC918",
            "\uC694\uCCAD\uD558\uC2E0 \uB0B4\uC6A9\uC774 \uC5C6\uC5B4\uC11C \uAD6C\uD604 \uAC00\uB2A5\uD55C \uC8FC\uC694 \uAE30\uB2A5\uB4E4\uC744 \uC548\uB0B4\uD574 \uB4DC\uB9BD\uB2C8\uB2E4.",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_RetriesIdentityAndToolInventoryAfterCodingTask()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryGenericGreetingFallback(
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uD648\uD398\uC774\uC9C0\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            "AgentQ\uB294 robot0971-art\uAC00 \uAC1C\uBC1C\uD55C Windows \uB370\uC2A4\uD06C\uD1B1 \uCF54\uB529 \uC5B4\uC2DC\uC2A4\uD134\uD2B8\uC785\uB2C8\uB2E4.\n\n### \uC81C\uAC00 \uAC00\uC9C4 \uD234 \uBAA9\uB85D\n- read_file\n- write_file\n- grep_search",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_RetriesSystemPromptSummaryAfterCodingTask()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryGenericGreetingFallback(
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uD648\uD398\uC774\uC9C0\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            "\uC81C \uC2DC\uC2A4\uD15C \uD504\uB86C\uD504\uD2B8\uB294 \uB2E4\uC74C\uACFC \uAC19\uC740 \uB0B4\uC6A9\uC73C\uB85C \uAD6C\uC131\uB418\uC5B4 \uC788\uC2B5\uB2C8\uB2E4.\n\n**\uAE30\uBCF8 \uC815\uCCB4\uC131**\n- \uC800\uB294 AgentQ Desktop\uC785\uB2C8\uB2E4.\n\n**\uC0AC\uC6A9 \uAC00\uB2A5\uD55C \uB3C4\uAD6C**\n- list_directory\n- bash",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_RetriesAnyNoToolFeatureAnswerBeforeWorkspaceAction()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryNoToolCodingFallback(
            "Create a Unity portfolio homepage with React JavaScript",
            "I will build a Vite + React + JavaScript portfolio with Hero, About, Projects, Skills, and Contact sections.",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_RejectsNoToolCodingCompletionAfterRetry()
    {
        var shouldReject = DesktopAgentService.ShouldRejectNoToolCodingCompletion(
            "Create a Unity portfolio homepage with React JavaScript",
            "I can create this as a Vite + React + JavaScript portfolio site.",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldReject);
    }

    [Fact]
    public void DesktopAgentService_RetriesFuturePromiseWithKoreanCreateWord()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryNoToolCodingFallback(
            "\uC5EC\uAE30\uC5D0 \uC0C8\uB85C\uC6B4 \uD504\uB85C\uC81D\uD2B8\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            "\uD604\uC7AC \uC6CC\uD06C\uC2A4\uD398\uC774\uC2A4\uAC00 \uBE44\uC5B4 \uC788\uC73C\uBBC0\uB85C Vite + React + JavaScript \uC2A4\uD0C0\uD130 \uD504\uB85C\uC81D\uD2B8\uB97C \uC0DD\uC131\uD558\uACA0\uC2B5\uB2C8\uB2E4.",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_AllowsFocusedQuestionForBareNewProject()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryNoToolCodingFallback(
            "\uC5EC\uAE30\uC5D0 \uC0C8\uB85C\uC6B4 \uD504\uB85C\uC81D\uD2B8\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            "\uC88B\uC2B5\uB2C8\uB2E4. \uC5B4\uB5A4 \uC885\uB958\uC758 \uD504\uB85C\uC81D\uD2B8\uB97C \uC6D0\uD558\uC2DC\uB098\uC694? \uC608: \uC6F9\uC0AC\uC774\uD2B8, Python \uB370\uC774\uD130 \uB3C4\uAD6C, API, \uAC8C\uC784, CLI \uB3C4\uAD6C",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        var shouldReject = DesktopAgentService.ShouldRejectNoToolCodingCompletion(
            "\uC5EC\uAE30\uC5D0 \uC0C8\uB85C\uC6B4 \uD504\uB85C\uC81D\uD2B8\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            "\uC88B\uC2B5\uB2C8\uB2E4. \uC5B4\uB5A4 \uC885\uB958\uC758 \uD504\uB85C\uC81D\uD2B8\uB97C \uC6D0\uD558\uC2DC\uB098\uC694? \uC608: \uC6F9\uC0AC\uC774\uD2B8, Python \uB370\uC774\uD130 \uB3C4\uAD6C, API, \uAC8C\uC784, CLI \uB3C4\uAD6C",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.False(shouldRetry);
        Assert.False(shouldReject);
    }

    [Fact]
    public void DesktopAgentService_RejectsFuturePromiseAfterRetry()
    {
        var shouldReject = DesktopAgentService.ShouldRejectNoToolCodingCompletion(
            "\uAC1C\uBC1C\uC790 \uAE30\uBCF8 \uB2E8\uC5B4\uC7A5 \uC6F9",
            "\uC5B4\uB5A4 \uC885\uB958\uC758 \uD504\uB85C\uC81D\uD2B8\uC778\uC9C0 \uBA85\uD655\uD574\uC84C\uB124\uC694. \uAC1C\uBC1C\uC790 \uAE30\uBCF8 \uB2E8\uC5B4\uC7A5 \uC6F9\uC744 Vite + React + JavaScript\uB85C \uB9CC\uB4E4\uC5B4 \uB4DC\uB9AC\uACA0\uC2B5\uB2C8\uB2E4.",
            executedToolCount: 1,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldReject);
    }

    [Fact]
    public void DesktopAgentService_RetriesGenericGreetingAfterReadOnlyToolUse()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryGenericGreetingFallback(
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uD648\uD398\uC774\uC9C0 \uC0DD\uC131",
            "\uC548\uB155\uD558\uC138\uC694! \uBB34\uC5C7\uC744 \uB3C4\uC640\uB4DC\uB9B4\uAE4C\uC694?",
            executedToolCount: 1,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_RetriesGenericGreetingForPythonDataToolRequest()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryGenericGreetingFallback(
            "\uD30C\uC774\uC36C\uC73C\uB85C \uAC04\uB2E8\uD55C \uB370\uC774\uD130 \uBD84\uC11D \uB3C4\uAD6C\uB97C \uB9CC\uB4E4\uC5B4 \uBCF4\uC790",
            "\uC548\uB155\uD558\uC138\uC694! \uBB34\uC5C7\uC744 \uB3C4\uC640\uB4DC\uB9B4\uAE4C\uC694?",
            executedToolCount: 0,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_RetriesAgentQIdentityGreetingAfterWorkspaceLook()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryGenericGreetingFallback(
            "\uC5EC\uAE30\uC5D0 \uB0B4 \uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uD648\uD398\uC774\uC9C0 \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            "\uBA3C\uC800 \uC791\uC5C5 \uACF5\uAC04\uC744 \uC0B4\uD3B4\uBCF4\uACA0\uC2B5\uB2C8\uB2E4.\uB124, \uB9DE\uC2B5\uB2C8\uB2E4! \uC800\uB294 AgentQ Desktop\uC785\uB2C8\uB2E4. robot0971-art \uB2D8\uC774 \uAC1C\uBC1C\uD55C Windows \uB370\uC2A4\uD06C\uD1B1 \uCF54\uB529 \uC5B4\uC2DC\uC2A4\uD134\uD2B8\uC785\uB2C8\uB2E4.\n\n\uBB34\uC5C7\uC744 \uB3C4\uC640\uB4DC\uB9B4\uAE4C\uC694?",
            executedToolCount: 1,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_RetriesGreetingForTerseKoreanProjectDirection()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryGenericGreetingFallback(
            "\uAC1C\uBC1C\uC790 \uAE30\uBCF8 \uB2E8\uC5B4\uC7A5 \uC6F9",
            "\uC548\uB155\uD558\uC138\uC694! AgentQ Desktop\uC785\uB2C8\uB2E4. \uBB34\uC5C7\uC744 \uB3C4\uC640\uB4DC\uB9B4\uAE4C\uC694?",
            executedToolCount: 1,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_RejectsNoChangeFeatureAnswerAfterReadOnlyToolUse()
    {
        var shouldReject = DesktopAgentService.ShouldRejectNoToolCodingCompletion(
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uD648\uD398\uC774\uC9C0 \uC0DD\uC131",
            "\uD604\uC7AC \uD504\uB85C\uC81D\uD2B8\uB294 Vite + React \uAE30\uBC18\uC758 \uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uC571\uC73C\uB85C \uBCF4\uC785\uB2C8\uB2E4. \uC694\uCCAD\uD558\uC2E0 \uAE30\uB2A5\uC744 \uC54C\uB824\uC8FC\uC138\uC694.",
            executedToolCount: 2,
            fileChanges: [],
            AgentWorkMode.Coding,
            DesktopTaskKind.Feature);

        Assert.True(shouldReject);
    }

    [Fact]
    public void DesktopClipboardService_ReportsClipboardFailureWithoutThrowing()
    {
        var viewModel = new MainViewModel();
        var service = new DesktopClipboardService(_ => throw new InvalidOperationException("clipboard is busy"));

        var exception = Record.Exception(() => service.CopyMessage(
            viewModel,
            new ChatMessageViewModel
            {
                Role = "AgentQ",
                Content = "hello"
            }));

        Assert.Null(exception);
        Assert.Contains("Clipboard copy failed", viewModel.StatusText);
    }

    [Fact]
    public void DesktopClipboardService_RetriesTransientClipboardFailure()
    {
        var viewModel = new MainViewModel();
        var attempts = 0;
        var service = new DesktopClipboardService(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new InvalidOperationException("clipboard is busy");
            }
        });

        service.CopyText(viewModel, "hello", "copied");

        Assert.Equal(3, attempts);
        Assert.Equal("copied", viewModel.StatusText);
    }

    [Fact]
    public void DesktopClipboardService_ReportsNothingToCopyForEmptyMessage()
    {
        var viewModel = new MainViewModel();
        var service = new DesktopClipboardService(_ => throw new InvalidOperationException("should not copy"));

        service.CopyMessage(
            viewModel,
            new ChatMessageViewModel
            {
                Role = "AgentQ",
                Content = string.Empty
            });

        Assert.Equal("Nothing to copy", viewModel.StatusText);
    }

    [Fact]
    public void DesktopAgentService_DoesNotRetryManualFallbackAfterToolsRan()
    {
        var shouldRetry = DesktopAgentService.ShouldRetryManualFallback(
            "\uC544\uB798\uCC98\uB7FC \uC218\uC815\uD558\uC138\uC694.\n```csharp\nDeliberateAgentQCompileBreak();\n```",
            executedToolCount: 1,
            fileChanges: [],
            AgentWorkMode.Coding);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void DesktopAgentService_TruncatedToolResultSavesFullOutputForInspection()
    {
        var root = CreateTempDirectory();
        var fullOutput = new string('x', 25000);

        var preview = DesktopAgentService.TruncateToolResult(
            fullOutput,
            root,
            out var wasTruncated,
            out var savedPath);

        Assert.True(wasTruncated);
        Assert.NotNull(savedPath);
        Assert.True(File.Exists(savedPath));
        Assert.StartsWith(Path.Combine(root, ".agentq", "tool-output"), savedPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fullOutput, File.ReadAllText(savedPath));
        Assert.Contains("[tool result truncated]", preview, StringComparison.Ordinal);
        Assert.Contains("Full output saved to:", preview, StringComparison.Ordinal);
        Assert.Contains("read_file with offset/limit", preview, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", 0, true)]
    [InlineData("   ", 0, true)]
    [InlineData("done", 0, false)]
    [InlineData("", 1, false)]
    public void DesktopAgentService_RetriesEmptyModelResponses(string text, int toolUseCount, bool expected)
    {
        Assert.Equal(expected, DesktopAgentService.ShouldRetryEmptyResponse(text, toolUseCount));
    }

    [Fact]
    public void DesktopToolCapabilitySnapshot_DescribesCurrentWorkModePermissions()
    {
        var registry = new ToolRegistry();
        registry.Register(new ListDirectoryTool());
        registry.Register(new DesktopProjectScaffoldPlanTool(Path.GetTempPath()));
        registry.Register(new ReadFileTool());
        registry.Register(new EditFileTool());
        registry.Register(new BashTool());
        registry.Register(new DesktopSymbolSearchTool(Path.GetTempPath()));
        registry.TryRegister(new DesktopSymbolSearchTool(Path.GetTempPath()));

        var prompt = DesktopToolCapabilitySnapshot.Create(registry, AgentWorkMode.Coding).ToPromptBlock();

        Assert.Contains("Tool Permission State:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allowed:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list_directory", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plan_project_scaffold", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("symbol_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requires approval:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("edit_file", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bash", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skipped duplicate tool registrations: symbol_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PowerShell", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&&", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiAgentRolePlanner_BuildsFeatureRoleChecklist()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("implement settings sync feature");
        var rolePlan = MultiAgentRolePlanner.Build(profile);

        Assert.Equal(DesktopTaskKind.Feature, rolePlan.Kind);
        Assert.Contains(rolePlan.Steps, step => step.Role == MultiAgentRole.Planner);
        Assert.Contains(rolePlan.Steps, step => step.Role == MultiAgentRole.Coder);
        Assert.Contains(rolePlan.Steps, step => step.Role == MultiAgentRole.Reviewer && step.IsParallelCandidate);
        Assert.Contains(rolePlan.Steps, step => step.Role == MultiAgentRole.Tester);
        Assert.Contains("Multi-agent role plan", rolePlan.FormatForPrompt(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopPromptAssemblyService_AddsMultiAgentRoleRules()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("code review this change");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base prompt", profile);

        Assert.Equal(DesktopTaskKind.CodeReview, profile.Kind);
        Assert.Contains("Multi-agent role plan", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reviewer", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tester", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not claim separate agents ran", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopPromptAssemblyService_AddsLinkCapabilityRulesForGeneralTasks()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("can you read this link?");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base prompt", profile);

        Assert.Equal(DesktopTaskKind.General, profile.Kind);
        Assert.Contains("Link handling rules", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fetch HTTP/HTTPS URLs", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never answer that AgentQ categorically cannot access external websites", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If no URL is present, ask the user to send the URL", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("Please read https://example.com/page.", true)]
    [InlineData("no link here", false)]
    public void LinkContentFetcher_DetectsHttpUrls(string text, bool expected)
    {
        Assert.Equal(expected, LinkContentFetcher.ContainsUrl(text));
    }

    [Fact]
    public void DesktopPromptAssemblyService_AddsVerificationFailureRules()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("verify command output and fix the failing check");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base prompt", profile);

        Assert.Equal(DesktopTaskKind.VerificationFailure, profile.Kind);
        Assert.Contains("Execution strategy (verification failure)", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Classify the failure", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verification failure response rules", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prefer fixing one failure class at a time", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("narrowest useful verification command", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopExecutionStrategyCatalog_ReturnsAnalysisWorkflow()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("analyze the architecture");
        var strategy = DesktopExecutionStrategyCatalog.ForProfile(profile);

        Assert.Equal(DesktopTaskKind.Analysis, strategy.Kind);
        Assert.Contains(strategy.Steps, step => step.Contains("workspace snapshot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(strategy.Steps, step => step.Contains("confirmed facts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DesktopPromptAssemblyService_AddsEvidenceRulesForAnalysis()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("analyze this project architecture");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base prompt", profile);

        Assert.Equal(DesktopTaskKind.Analysis, profile.Kind);
        Assert.Contains("Evidence-backed response rules", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Evidence section", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Needs verification", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inspected files", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not claim", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopPromptAssemblyService_AddsEvidenceRulesForDocumentation()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("update README with the current stack");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base prompt", profile);

        Assert.Equal(DesktopTaskKind.Documentation, profile.Kind);
        Assert.Contains("Evidence-backed response rules", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supporting file", prompt, StringComparison.OrdinalIgnoreCase);
    }

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MainViewModel_ToConfiguration_PersistsAutoFetchLinks(bool enabled)
    {
        var viewModel = new MainViewModel
        {
            AutoFetchLinks = enabled
        };

        var config = viewModel.ToConfiguration();

        Assert.Equal(enabled, config.DesktopAutoFetchLinks);
    }

    [Fact]
    public void MainViewModel_ToConfiguration_PersistsEmbeddingSettingsSeparately()
    {
        var viewModel = new MainViewModel
        {
            Provider = "opencode-go",
            ApiKey = "chat-key",
            EmbeddingProvider = "openai",
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingBaseUrl = "https://api.openai.test/v1",
            EmbeddingApiKey = "embedding-key"
        };

        var config = viewModel.ToConfiguration();

        Assert.Equal("opencode-go", config.Provider);
        Assert.Equal("chat-key", config.ApiKey);
        Assert.Equal("openai", config.EmbeddingProvider);
        Assert.Equal("text-embedding-3-small", config.EmbeddingModel);
        Assert.Equal("https://api.openai.test/v1", config.EmbeddingBaseUrl);
        Assert.Equal("embedding-key", config.EmbeddingApiKey);
    }

    [Fact]
    public void MainViewModel_StatusAccentBrush_HighlightsErrorStatus()
    {
        var viewModel = new MainViewModel
        {
            StatusText = "Embedding index failed: model not found"
        };

        Assert.Equal("#F87171", viewModel.StatusAccentBrush);
    }

    [Fact]
    public void ModelReasoningTagFilter_StripsThinkTagsFromProviderOutput()
    {
        var text = "李얠븘蹂닿쿋?듬땲??</think>`EmbeddingIndexBuilder.cs` ?뺤씤<think>hidden</think>?꾨즺";

        var filtered = ModelReasoningTagFilter.Strip(text);

        Assert.Equal("李얠븘蹂닿쿋?듬땲??`EmbeddingIndexBuilder.cs` ?뺤씤?꾨즺", filtered);
    }

    [Fact]
    public void DesktopProviderModelCatalog_ProvidesDefaultsForKnownAndUnknownProviders()
    {
        Assert.Contains("opencode-go", DesktopProviderModelCatalog.Providers);
        Assert.Equal("kimi-k2.6", DesktopProviderModelCatalog.GetDefaultModel("opencode-go"));
        Assert.Contains("qwen3.6-plus", DesktopProviderModelCatalog.GetModels("opencode-go"));
        Assert.Contains("gpt-5.5", DesktopProviderModelCatalog.GetModels("openai"));
        Assert.Contains("gpt-5.4-mini", DesktopProviderModelCatalog.GetModels("openai"));
        Assert.Contains("gpt-5.3-codex", DesktopProviderModelCatalog.GetModels("openai"));
        Assert.Contains("claude-opus-4-7", DesktopProviderModelCatalog.GetModels("anthropic"));
        Assert.Contains("claude-sonnet-4-6", DesktopProviderModelCatalog.GetModels("anthropic"));
        Assert.Contains("gemini-3.1-pro-preview", DesktopProviderModelCatalog.GetModels("google"));
        Assert.Contains("gemini-3-flash-preview", DesktopProviderModelCatalog.GetModels("google"));
        Assert.Contains("grok-4.3", DesktopProviderModelCatalog.GetModels("xai"));
        Assert.Contains("deepseek-v4-pro", DesktopProviderModelCatalog.GetModels("deepseek"));
        Assert.Equal("https://api.openai.com/v1", DesktopProviderModelCatalog.GetDefaultBaseUrl("openai", string.Empty));
        Assert.Equal("default", DesktopProviderModelCatalog.GetDefaultModel("custom-provider"));
        Assert.Equal("https://example.test", DesktopProviderModelCatalog.GetDefaultBaseUrl("custom-provider", "https://example.test"));
    }

    [Fact]
    public async Task WorkspaceAnalysisService_BuildsProjectMapAndKeyFiles()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "api"));
        Directory.CreateDirectory(Path.Combine(root, "tests"));
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Test");
        await File.WriteAllTextAsync(Path.Combine(root, "package.json"), """{"scripts":{"test":"echo test"}}""");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("UI layer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("API layer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Tests", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("evidence: src", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("evidence: api", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("README.md", analysis.KeyFiles);
        Assert.Contains("package.json", analysis.KeyFiles);
    }

    [Fact]
    public async Task WorkspaceAnalysisService_DetectsMultiLanguageProjects()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "include"));
        Directory.CreateDirectory(Path.Combine(root, "cmd"));
        Directory.CreateDirectory(Path.Combine(root, "Source"));
        Directory.CreateDirectory(Path.Combine(root, "Content"));
        Directory.CreateDirectory(Path.Combine(root, "Config"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "package.json"),
            """{"dependencies":{"next":"latest","react":"latest"},"devDependencies":{"typescript":"latest"},"scripts":{"build":"next build","lint":"next lint","test":"vitest"}}""");
        await File.WriteAllTextAsync(Path.Combine(root, "pyproject.toml"), """[project]\ndependencies = ["fastapi"]""");
        await File.WriteAllTextAsync(Path.Combine(root, "go.mod"), "module example.com/app");
        await File.WriteAllTextAsync(Path.Combine(root, "Cargo.toml"), "[package]");
        await File.WriteAllTextAsync(Path.Combine(root, "CMakeLists.txt"), "cmake_minimum_required(VERSION 3.20)");
        await File.WriteAllTextAsync(Path.Combine(root, "Game.uproject"), "{}");
        await File.WriteAllTextAsync(Path.Combine(root, "include", "game.hpp"), "#pragma once");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "game.cpp"), "int main() { return 0; }");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains("Node", analysis.ProjectType);
        Assert.Contains("Python", analysis.ProjectType);
        Assert.Contains("C++", analysis.ProjectType);
        Assert.Contains("Go", analysis.ProjectType);
        Assert.Contains("Rust", analysis.ProjectType);
        Assert.Contains("Unreal", analysis.ProjectType);
        Assert.Contains("Next.js", analysis.Framework);
        Assert.Contains("FastAPI", analysis.Framework);
        Assert.Contains("CMake", analysis.Framework);
        Assert.Contains(analysis.VerificationCommands, command => command.Contains("npm run build", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.VerificationCommands, command => command.Contains("python -m pytest", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.VerificationCommands, command => command.Contains("cmake --build build", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.VerificationCommands, command => command.Contains("go test ./...", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.VerificationCommands, command => command.Contains("cargo test", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("C++ headers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Go packages", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Unreal project", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("CMakeLists.txt", analysis.KeyFiles);
        Assert.Contains("go.mod", analysis.KeyFiles);
        Assert.Contains("Cargo.toml", analysis.KeyFiles);
        Assert.Contains("Game.uproject", analysis.KeyFiles);
    }

    [Fact]
    public async Task WorkspaceAnalysisService_MapsUnityGameProjectDetails()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scenes"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Prefabs"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scripts"));
        Directory.CreateDirectory(Path.Combine(root, "Packages"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"),
            "m_EditorVersion: 2023.2.1f1");
        await File.WriteAllTextAsync(
            Path.Combine(root, "ProjectSettings", "EditorBuildSettings.asset"),
            """
            EditorBuildSettings:
              m_Scenes:
              - enabled: 1
                path: Assets/Scenes/Main.unity
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Packages", "manifest.json"),
            """
            {
              "dependencies": {
                "com.unity.inputsystem": "1.7.0",
                "com.unity.render-pipelines.universal": "14.0.0",
                "com.unity.test-framework": "1.1.33"
              }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "Assets", "Scenes", "Main.unity"), "%YAML 1.1");
        await File.WriteAllTextAsync(Path.Combine(root, "Assets", "Prefabs", "Player.prefab"), "%YAML 1.1");
        await File.WriteAllTextAsync(
            Path.Combine(root, "Assets", "Scripts", "PlayerController.cs"),
            """
            using UnityEngine;
            public sealed class PlayerController : MonoBehaviour { }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Assets", "Scripts", "Game.Runtime.asmdef"),
            """{"name":"Game.Runtime","references":[]}""");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains("Unity", analysis.ProjectType);
        Assert.Contains("Unity 2023.2.1f1", analysis.Framework);
        Assert.Contains("Unity Input System", analysis.Framework);
        Assert.Contains("Unity URP", analysis.Framework);
        Assert.Contains("Unity Test Framework", analysis.Framework);
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Unity scenes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Unity prefabs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Unity scripts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Unity asmdefs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeyFiles, file => file == "Assets/Scenes/Main.unity");
        Assert.Contains(analysis.KeyFiles, file => file == "Assets/Prefabs/Player.prefab");
        Assert.Contains(analysis.KeyFiles, file => file == "Assets/Scripts/PlayerController.cs");
        Assert.Contains(analysis.KeyFiles, file => file == "Assets/Scripts/Game.Runtime.asmdef");
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("unity assembly Game.Runtime", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeyDependencies, dependency => dependency.Contains("com.unity.inputsystem", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeyDependencies, dependency => dependency.Contains("Assets/Scenes/Main.unity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Hints, hint => hint.Contains("Unity verification hint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NativeWorkerHost_ExtractsCppGoAndRustFoundations()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "build"));
        Directory.CreateDirectory(Path.Combine(root, "include"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "cmd", "app"));
        Directory.CreateDirectory(Path.Combine(root, "rust", "src"));
        Directory.CreateDirectory(Path.Combine(root, "java", "src", "main", "java", "demo"));
        Directory.CreateDirectory(Path.Combine(root, "db", "migrations"));
        Directory.CreateDirectory(Path.Combine(root, "php", "app"));
        Directory.CreateDirectory(Path.Combine(root, "kotlin", "src", "main", "kotlin"));
        Directory.CreateDirectory(Path.Combine(root, "swift", "Sources", "Demo"));
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        Directory.CreateDirectory(Path.Combine(root, "R"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "CMakeLists.txt"),
            """
            cmake_minimum_required(VERSION 3.20)
            project(native_demo)
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "build", "compile_commands.json"),
            """[{"directory":".","command":"c++ -c src/main.cpp","file":"src/main.cpp"}]""");
        await File.WriteAllTextAsync(Path.Combine(root, "include", "demo.hpp"), "#pragma once");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "main.cpp"), "int main() { return 0; }");
        await File.WriteAllTextAsync(
            Path.Combine(root, "go.mod"),
            """
            module example.com/native
            go 1.23
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "cmd", "app", "main.go"), "package main\nfunc main() {}");
        await File.WriteAllTextAsync(
            Path.Combine(root, "rust", "Cargo.toml"),
            """
            [package]
            name = "native-rust"
            version = "0.1.0"
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "rust", "src", "lib.rs"), "pub fn ok() -> bool { true }");
        await File.WriteAllTextAsync(Path.Combine(root, "java", "pom.xml"), "<project><dependencies><dependency><artifactId>spring-boot-starter-web</artifactId></dependency><dependency><artifactId>junit-jupiter</artifactId></dependency></dependencies></project>");
        await File.WriteAllTextAsync(Path.Combine(root, "java", "src", "main", "java", "demo", "DemoController.java"), "package demo; public class DemoController {}");
        await File.WriteAllTextAsync(Path.Combine(root, "db", "migrations", "001_create_users.sql"), "create table users (id integer primary key);");
        await File.WriteAllTextAsync(Path.Combine(root, "php", "composer.json"), """{"require":{"laravel/framework":"^11.0"},"require-dev":{"phpunit/phpunit":"^11.0"}}""");
        await File.WriteAllTextAsync(Path.Combine(root, "php", "app", "UserController.php"), "<?php class UserController {} function route_users() {}");
        await File.WriteAllTextAsync(Path.Combine(root, "kotlin", "build.gradle.kts"), """plugins { id("io.ktor.plugin") version "2.3.0" }""");
        await File.WriteAllTextAsync(Path.Combine(root, "kotlin", "src", "main", "kotlin", "Application.kt"), "class Application\nfun main() {}");
        await File.WriteAllTextAsync(Path.Combine(root, "swift", "Package.swift"), "// swift-tools-version: 5.9");
        await File.WriteAllTextAsync(Path.Combine(root, "swift", "Sources", "Demo", "ContentView.swift"), "import SwiftUI\nstruct ContentView: View {}");
        await File.WriteAllTextAsync(Path.Combine(root, "scripts", "build.ps1"), "function Invoke-Build { Write-Host build }");
        await File.WriteAllTextAsync(Path.Combine(root, "R", "analysis.R"), "summarise_users <- function(data) { data }");

        var result = await new NativeWorkerHost().AnalyzeAsync(root, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Cpp.CmakeProjects, project => project.Name == "native_demo");
        Assert.Contains(result.Cpp.CompileCommands, item => item.Path == "build/compile_commands.json" && item.Count == 1);
        Assert.Contains(result.Cpp.SourceFiles, file => file == "src/main.cpp");
        Assert.Contains(result.Cpp.HeaderFiles, file => file == "include/demo.hpp");
        Assert.Contains(result.Go.Modules, module => module.Module == "example.com/native" && module.GoVersion == "1.23");
        Assert.Contains(result.Go.SourceFiles, file => file == "cmd/app/main.go");
        Assert.Contains(result.Rust.Manifests, manifest => manifest.Path == "rust/Cargo.toml" &&
                                                           manifest.PackageName == "native-rust");
        Assert.Contains(result.Rust.SourceFiles, file => file == "rust/src/lib.rs");
        Assert.Contains(result.ProjectMap, entry => entry.Role == "C++ compile database");
        Assert.Contains(result.ProjectMap, entry => entry.Role == "Go modules");
        Assert.Contains(result.ProjectMap, entry => entry.Role == "Cargo manifests");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-cpp-cmake-target");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-go-service");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-rust-crate-feature");
        Assert.Contains(result.Java.Symbols, symbol => symbol.Name == "DemoController");
        Assert.Contains(result.Sql.Tables, table => table.Name == "users");
        Assert.Contains(result.Php.Frameworks, framework => framework == "Laravel");
        Assert.Contains(result.Kotlin.Symbols, symbol => symbol.Name == "Application");
        Assert.Contains(result.Swift.Frameworks, framework => framework == "SwiftUI");
        Assert.Contains(result.Scripts.Commands, command => command.Name == "Invoke-Build");
        Assert.Contains(result.R.Symbols, symbol => symbol.Name == "summarise_users");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-java-service");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-sql-migration");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-php-feature");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-kotlin-feature");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-swift-feature");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-automation-script");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-r-analysis");
        Assert.Contains(result.ScaffoldRecommendations, recommendation => recommendation.Name == "Rust crate feature");
    }

    [Fact]
    public async Task WorkspaceAnalysisService_UsesNativeWorkerResults()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "build"));
        Directory.CreateDirectory(Path.Combine(root, "include"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "cmd", "app"));
        Directory.CreateDirectory(Path.Combine(root, "rust", "src"));
        Directory.CreateDirectory(Path.Combine(root, "java", "src", "main", "java", "demo"));
        Directory.CreateDirectory(Path.Combine(root, "db", "migrations"));
        Directory.CreateDirectory(Path.Combine(root, "php", "app"));
        Directory.CreateDirectory(Path.Combine(root, "kotlin", "src", "main", "kotlin"));
        Directory.CreateDirectory(Path.Combine(root, "swift", "Sources", "Demo"));
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        Directory.CreateDirectory(Path.Combine(root, "R"));
        await File.WriteAllTextAsync(Path.Combine(root, "CMakeLists.txt"), "project(native_demo)");
        await File.WriteAllTextAsync(
            Path.Combine(root, "build", "compile_commands.json"),
            """[{"directory":".","command":"c++ -c src/main.cpp","file":"src/main.cpp"}]""");
        await File.WriteAllTextAsync(Path.Combine(root, "include", "demo.hpp"), "#pragma once");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "main.cpp"), "int main() { return 0; }");
        await File.WriteAllTextAsync(Path.Combine(root, "go.mod"), "module example.com/native\ngo 1.23");
        await File.WriteAllTextAsync(Path.Combine(root, "cmd", "app", "main.go"), "package main\nfunc main() {}");
        await File.WriteAllTextAsync(
            Path.Combine(root, "rust", "Cargo.toml"),
            """
            [package]
            name = "native-rust"
            version = "0.1.0"
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "rust", "src", "lib.rs"), "pub fn ok() -> bool { true }");
        await File.WriteAllTextAsync(Path.Combine(root, "java", "pom.xml"), "<project><dependencies><dependency><artifactId>spring-boot-starter-web</artifactId></dependency></dependencies></project>");
        await File.WriteAllTextAsync(Path.Combine(root, "java", "src", "main", "java", "demo", "DemoController.java"), "package demo; public class DemoController {}");
        await File.WriteAllTextAsync(Path.Combine(root, "db", "migrations", "001_create_users.sql"), "create table users (id integer primary key);");
        await File.WriteAllTextAsync(Path.Combine(root, "php", "composer.json"), """{"require":{"laravel/framework":"^11.0"}}""");
        await File.WriteAllTextAsync(Path.Combine(root, "php", "app", "UserController.php"), "<?php class UserController {}");
        await File.WriteAllTextAsync(Path.Combine(root, "kotlin", "build.gradle.kts"), """plugins { id("io.ktor.plugin") version "2.3.0" }""");
        await File.WriteAllTextAsync(Path.Combine(root, "kotlin", "src", "main", "kotlin", "Application.kt"), "class Application");
        await File.WriteAllTextAsync(Path.Combine(root, "swift", "Package.swift"), "// swift-tools-version: 5.9");
        await File.WriteAllTextAsync(Path.Combine(root, "swift", "Sources", "Demo", "ContentView.swift"), "import SwiftUI\nstruct ContentView: View {}");
        await File.WriteAllTextAsync(Path.Combine(root, "scripts", "build.ps1"), "function Invoke-Build { Write-Host build }");
        await File.WriteAllTextAsync(Path.Combine(root, "R", "analysis.R"), "summarise_users <- function(data) { data }");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains(analysis.Hints, hint => hint.Contains("Native worker indexed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("C++ compile database", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Go modules", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Cargo manifests", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeyFiles, file => file == "build/compile_commands.json");
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("cmake project native_demo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("go module example.com/native", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("cargo native-rust", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeyDependencies, dependency => dependency.Contains("compile command", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Hints, hint => hint.Contains("Native worker capability: create-cpp-cmake-target", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Suggested scaffold: Rust crate feature", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Java", analysis.ProjectType);
        Assert.Contains("SQL", analysis.ProjectType);
        Assert.Contains("PHP", analysis.ProjectType);
        Assert.Contains("Kotlin", analysis.ProjectType);
        Assert.Contains("Swift", analysis.ProjectType);
        Assert.Contains("R", analysis.ProjectType);
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("java class DemoController", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("sql table users", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("php class UserController", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("kotlin class Application", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("swift struct ContentView", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("script command Invoke-Build", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("r function summarise_users", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WorkspaceAnalysisService_DetectsNestedFrontendBackendWithoutDependencyNoise()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "frontend"));
        Directory.CreateDirectory(Path.Combine(root, "frontend", "src"));
        Directory.CreateDirectory(Path.Combine(root, "frontend", "node_modules", "native"));
        Directory.CreateDirectory(Path.Combine(root, "backend"));
        Directory.CreateDirectory(Path.Combine(root, "backend", "app"));
        Directory.CreateDirectory(Path.Combine(root, "backend", "app", "models"));
        Directory.CreateDirectory(Path.Combine(root, "backend", "app", "schemas"));
        Directory.CreateDirectory(Path.Combine(root, "backend", "alembic"));
        Directory.CreateDirectory(Path.Combine(root, "backend", ".venv", "Lib", "site-packages", "native"));
        await File.WriteAllTextAsync(Path.Combine(root, "docker-compose.yml"), "services: {}");
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "package.json"),
            """{"dependencies":{"react":"latest"},"devDependencies":{"vite":"latest"},"scripts":{"build":"vite build"}}""");
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "requirements.txt"),
            """
            fastapi==0.111.0
            sqlalchemy==2.0.30
            alembic==1.13.1
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "backend", "alembic.ini"), "[alembic]");
        await File.WriteAllTextAsync(Path.Combine(root, "backend", "app", "main.py"), "from fastapi import FastAPI");
        await File.WriteAllTextAsync(Path.Combine(root, "backend", ".venv", "Lib", "site-packages", "native", "noise.h"), "#pragma once");
        await File.WriteAllTextAsync(Path.Combine(root, "frontend", "node_modules", "native", "noise.hpp"), "#pragma once");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains("Node", analysis.ProjectType);
        Assert.Contains("Python", analysis.ProjectType);
        Assert.DoesNotContain("C++", analysis.ProjectType);
        Assert.Contains("Docker", analysis.ProjectType);
        Assert.Contains("Vite", analysis.Framework);
        Assert.Contains("FastAPI", analysis.Framework);
        Assert.Contains("SQLAlchemy", analysis.Framework);
        Assert.Contains("Alembic", analysis.Framework);
        Assert.Contains("Docker Compose", analysis.Framework);
        Assert.Contains(analysis.VerificationCommands, command => command.Contains("cd frontend", StringComparison.OrdinalIgnoreCase) &&
                                                                  command.Contains("npm run build", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.VerificationCommands, command => command.Contains("docker compose config", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Frontend", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Backend", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Database models", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Database migrations", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("evidence: frontend", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("evidence: backend/app/models", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Hints, hint => hint.Contains("Frontend/backend workspace", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Path.Combine("frontend", "package.json"), analysis.KeyFiles);
        Assert.Contains(Path.Combine("backend", "requirements.txt"), analysis.KeyFiles);
    }

    [Fact]
    public async Task WorkspaceAnalysisService_DoesNotLabelPythonSrcAsCppSource()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "tests"));
        await File.WriteAllTextAsync(Path.Combine(root, "pyproject.toml"), """[project]\ndependencies = ["fastapi", "sqlalchemy"]""");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "main.py"), "from fastapi import FastAPI");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains("Python", analysis.ProjectType);
        Assert.DoesNotContain("C++", analysis.ProjectType);
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Python packages", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(analysis.ProjectMap, entry => entry.Contains("C++ source", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WorkspaceAnalysisService_BuildsCSharpSymbolIndex()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "AuthService.cs"),
            """
            namespace Demo;

            public sealed class AuthService
            {
                public Task<bool> LoginAsync(string email) => Task.FromResult(true);
            }

            public record LoginRequest(string Email);
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "bin", "Noise.cs"),
            """
            public sealed class BuildOutputNoise
            {
                public void IgnoreMe() { }
            }
            """);

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.True(analysis.SymbolCount >= 3);
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("class AuthService", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("method AuthService.LoginAsync", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("record LoginRequest", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(analysis.KeySymbols, symbol => symbol.Contains("BuildOutputNoise", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Hints, hint => hint.Contains("symbol index", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CSharpRoslynAnalysisService_ExtractsSymbolsReferencesAndDiagnostics()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "App"));
        Directory.CreateDirectory(Path.Combine(root, "Lib"));
        File.WriteAllText(
            Path.Combine(root, "App", "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Lib", "Lib.csproj"), """<Project Sdk="Microsoft.NET.Sdk" />""");
        File.WriteAllText(
            Path.Combine(root, "App", "AuthService.cs"),
            """
            using Demo.Lib;

            namespace Demo.App;

            public sealed record LoginRequest(string Email);

            public sealed class AuthService
            {
                public AuthService() { }

                public Task<bool> LoginAsync(string email) => Task.FromResult(true);
            }
            """);
        File.WriteAllText(
            Path.Combine(root, "App", "Broken.cs"),
            """
            namespace Demo.App;
            public sealed class Broken {
            """);

        var analysis = new CSharpRoslynAnalysisService().Analyze(root);

        Assert.Contains(analysis.Projects, project => project == "App/App.csproj");
        Assert.Contains(analysis.ProjectReferences, reference => reference.Path == "App/App.csproj" &&
                                                                 reference.Target == "Lib/Lib.csproj");
        Assert.Contains(analysis.Namespaces, item => item.Name == "Demo.App");
        Assert.Contains(analysis.Symbols, symbol => symbol.Kind == "record" &&
                                                    symbol.Name == "LoginRequest");
        Assert.Contains(analysis.Symbols, symbol => symbol.Kind == "method" &&
                                                    symbol.Container == "AuthService" &&
                                                    symbol.Name == "LoginAsync");
        Assert.Contains(analysis.Usings, item => item.Namespace == "Demo.Lib");
        Assert.Contains(analysis.Diagnostics, item => item.Id.StartsWith("CS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WorkspaceAnalysisService_UsesRoslynCSharpResults()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "App"));
        Directory.CreateDirectory(Path.Combine(root, "Lib"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "App", "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "Lib", "Lib.csproj"), """<Project Sdk="Microsoft.NET.Sdk" />""");
        await File.WriteAllTextAsync(
            Path.Combine(root, "App", "AuthService.cs"),
            """
            using Demo.Lib;

            namespace Demo.App;

            public sealed class AuthService
            {
                public bool Login() => true;
            }
            """);

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains(analysis.Hints, hint => hint.Contains("Roslyn C# analysis", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("C# projects", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("namespace Demo.App", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("method AuthService.Login", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeyDependencies, dependency => dependency.Contains("roslyn using", StringComparison.OrdinalIgnoreCase) &&
                                                                dependency.Contains("Demo.Lib", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeyDependencies, dependency => dependency.Contains("roslyn project-reference", StringComparison.OrdinalIgnoreCase) &&
                                                                dependency.Contains("Lib/Lib.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WorkspaceAnalysisService_BuildsPythonAndTypeScriptSymbolIndex()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "backend"));
        Directory.CreateDirectory(Path.Combine(root, "frontend"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "service.py"),
            """
            class StockService:
                async def refresh_prices(self):
                    return True

            def create_app():
                return object()
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "App.tsx"),
            """
            export class DashboardView {
                render() {
                    return null;
                }
            }

            export const useStocks = () => [];
            export function formatPrice(value: number) {
                return value.toString();
            }
            """);

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.True(analysis.SymbolCount >= 6);
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("class StockService", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("function StockService.refresh_prices", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("function create_app", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("class DashboardView", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("function useStocks", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("function formatPrice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkspaceDependencyGraphService_ExtractsJavaScriptPythonAndCSharpEdges()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "frontend", "src"));
        Directory.CreateDirectory(Path.Combine(root, "backend", "app", "services"));
        Directory.CreateDirectory(Path.Combine(root, "csharp", "App"));
        Directory.CreateDirectory(Path.Combine(root, "csharp", "Lib"));
        File.WriteAllText(
            Path.Combine(root, "frontend", "src", "Login.tsx"),
            """
            import { login } from "./auth";
            export { login } from "./auth";
            """);
        File.WriteAllText(Path.Combine(root, "frontend", "src", "auth.ts"), "export function login() { return true; }");
        File.WriteAllText(Path.Combine(root, "backend", "app", "main.py"), "from app.services.auth import login");
        File.WriteAllText(Path.Combine(root, "backend", "app", "services", "auth.py"), "def login(): return True");
        File.WriteAllText(
            Path.Combine(root, "csharp", "App", "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "csharp", "Lib", "Lib.csproj"), """<Project Sdk="Microsoft.NET.Sdk" />""");
        File.WriteAllText(Path.Combine(root, "csharp", "App", "Program.cs"), "using Demo.Lib;");

        var graph = new WorkspaceDependencyGraphService().Build(root);

        Assert.Contains(graph.Edges, edge =>
            edge.FromPath == "frontend/src/Login.tsx" &&
            edge.ToPath == "frontend/src/auth.ts" &&
            edge.Kind == "import");
        Assert.Contains(graph.Edges, edge =>
            edge.FromPath == "backend/app/main.py" &&
            edge.ToPath == "backend/app/services/auth.py" &&
            edge.Kind == "from-import");
        Assert.Contains(graph.Edges, edge =>
            edge.FromPath == "csharp/App/App.csproj" &&
            edge.Target == "csharp/Lib/Lib.csproj" &&
            edge.Kind == "project-reference");
        Assert.Contains(graph.Edges, edge =>
            edge.FromPath == "csharp/App/Program.cs" &&
            edge.Target == "Demo.Lib" &&
            edge.Kind == "using");
    }

    [Fact]
    public async Task WorkspaceAnalysisService_IncludesDependencyGraphSummary()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "App.tsx"), "import { login } from './auth';");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "auth.ts"), "export const login = () => true;");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.True(analysis.DependencyEdgeCount >= 1);
        Assert.Contains(analysis.KeyDependencies, dependency => dependency.Contains("src/App.tsx", StringComparison.OrdinalIgnoreCase) &&
                                                                dependency.Contains("src/auth.ts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Hints, hint => hint.Contains("Dependency graph", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TypeScriptWorkerHost_ExtractsPackageImportsComponentsAndRoutes()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "frontend", "src", "pages"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "package.json"),
            """
            {
              "name": "agentq-web",
              "dependencies": { "react": "latest" },
              "devDependencies": { "vite": "latest", "typescript": "latest", "@playwright/test": "latest" },
              "scripts": { "build": "vite build", "test": "vitest", "test:e2e": "playwright test" }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "playwright.config.ts"),
            "export default {};");
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "tsconfig.json"),
            """{"compilerOptions":{"jsx":"react-jsx","target":"ES2022","module":"ESNext","baseUrl":".","paths":{"@/*":["src/*"]}}}""");
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "src", "api.ts"),
            """
            export function loadDashboard() {
              return true;
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "src", "pages", "Dashboard.tsx"),
            """
            import React from 'react';
            import { loadDashboard } from '@/api';
            export { loadDashboard } from '@/api';
            const legacy = require('@/api');

            export function DashboardView() {
              return <main />;
            }

            export const useDashboard = () => [];

            async function loadRoute() {
              return import('@/api');
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "src", "pages", "Dashboard.test.tsx"),
            """
            describe("DashboardView", () => {
              it("loads", () => {});
            });
            """);
        Directory.CreateDirectory(Path.Combine(root, "frontend", "src", "app", "api", "users"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "src", "app", "api", "users", "route.ts"),
            """
            export async function GET() {
              return Response.json([]);
            }
            """);

        var result = await new TypeScriptWorkerHost().AnalyzeAsync(root, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Packages, package => package.Path == "frontend/package.json");
        Assert.Contains(result.Tsconfigs, config => config.Path == "frontend/tsconfig.json" &&
                                                    config.BaseUrl == "." &&
                                                    config.Paths.ContainsKey("@/*"));
        Assert.Contains(result.NpmScripts, script => script.Name == "build");
        Assert.Contains(result.Imports, import => import.Source == "react");
        Assert.Contains(result.Imports, import => import.Source == "@/api" &&
                                                  import.ResolvedPath == "frontend/src/api.ts");
        Assert.Contains(result.ReactComponents, component => component.Name == "DashboardView");
        Assert.Contains(result.Routes, route => route.Path.Contains("Dashboard.tsx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.ReactHooks, hook => hook.Name == "useDashboard");
        Assert.Contains(result.ApiEndpoints, endpoint => endpoint.Method == "GET" &&
                                                         endpoint.Route.Contains("/api/users", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.TestTargets, target => target.Kind == "it" &&
                                                     target.Name == "loads");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-react-feature");
        Assert.Contains(result.Capabilities, capability => capability.Name == "verify-playwright");
        Assert.True(result.Playwright.HasDependency);
        Assert.Contains(result.Playwright.Configs, config => config == "frontend/playwright.config.ts");
        Assert.Contains(result.Playwright.Scripts, script => script.Name == "test:e2e" &&
                                                            script.Command == "playwright test");
        Assert.Contains(result.ScaffoldRecommendations, recommendation => recommendation.Name == "React application feature");
        Assert.Contains(result.Exports, export => export.Name == "useDashboard");
        Assert.Contains(result.Exports, export => export.Name == "loadDashboard");
        Assert.Contains(result.Symbols, symbol => symbol.Name == "loadRoute");
    }

    [Fact]
    public async Task TypeScriptWorkerHost_RecommendsNewViteReactProjectForEmptyWorkspace()
    {
        var root = CreateTempDirectory();

        var result = await new TypeScriptWorkerHost().AnalyzeAsync(root, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-vite-react-project");
        Assert.Contains(result.ScaffoldRecommendations, recommendation =>
            recommendation.Name == "Vite React TypeScript project" &&
            recommendation.Files.Contains("package.json") &&
            recommendation.Files.Contains("src/App.tsx"));
    }

    [Fact]
    public async Task TypeScriptWorkerHost_RecommendsNewViteReactProjectForPackageOnlyWorkspace()
    {
        var root = CreateTempDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(root, "package.json"),
            """{"dependencies":{"react":"latest","vite":"latest"},"scripts":{"build":"vite build"}}""");

        var result = await new TypeScriptWorkerHost().AnalyzeAsync(root, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.ScaffoldRecommendations, recommendation =>
            recommendation.Name == "Vite React TypeScript project" &&
            recommendation.Files.Contains("index.html") &&
            recommendation.Files.Contains("src/main.tsx"));
    }

    [Fact]
    public async Task WorkspaceAnalysisService_PreservesWorkerScaffoldRecommendations()
    {
        var root = CreateTempDirectory();

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains(analysis.ScaffoldRecommendations, recommendation =>
            recommendation.Name == "Vite React TypeScript project" &&
            recommendation.Files.Contains("package.json"));
        Assert.Contains(analysis.ProjectMap, entry =>
            entry.Contains("Suggested scaffold: Vite React TypeScript project", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DesktopScaffoldIntentRouter_PrefersProjectScaffoldForPackageOnlyPortfolioRequest()
    {
        var root = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "package.json"), """{"dependencies":{"react":"latest"}}""");
        var recommendations = new List<WorkerScaffoldRecommendation>
        {
            new()
            {
                Name = "React application feature",
                Files =
                [
                    "<feature_dir>/<Feature>View.tsx",
                    "<feature_dir>/use<Feature>.ts"
                ],
                VerificationCommands = ["npm test"]
            },
            new()
            {
                Name = "Vite React TypeScript project",
                Files =
                [
                    "package.json",
                    "index.html",
                    "src/main.tsx",
                    "src/App.tsx"
                ],
                VerificationCommands = ["npm run build"]
            }
        };

        var selected = new DesktopScaffoldIntentRouter().SelectRecommendation(
            recommendations,
            "?ы듃?대━???앹꽦",
            root);

        Assert.Equal("Vite React TypeScript project", selected.Name);
    }

    [Fact]
    public async Task DesktopScaffoldIntentRouter_DoesNotForceProjectScaffoldForRunnableApp()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "package.json"), """{"dependencies":{"react":"latest"}}""");
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<div id=\"root\"></div>");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "main.tsx"), "export {};");

        var shouldHandle = new DesktopScaffoldIntentRouter().ShouldHandleLocally("\uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uC0DD\uC131", root);

        Assert.False(shouldHandle);
    }

    [Fact]
    public void DesktopScaffoldIntentRouter_AsksForBriefBeforeAmbiguousPortfolioScaffold()
    {
        var root = CreateTempDirectory();

        var shouldAsk = new DesktopScaffoldIntentRouter().ShouldAskForProjectBrief(
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uC0DD\uC131",
            root);

        Assert.True(shouldAsk);
    }

    [Fact]
    public void DesktopScaffoldIntentRouter_DoesNotAskBriefForExplicitReactProjectScaffold()
    {
        var root = CreateTempDirectory();

        var router = new DesktopScaffoldIntentRouter();
        var shouldAsk = router.ShouldAskForProjectBrief("React project create", root);
        var shouldHandle = router.ShouldHandleLocally("React project create", root);

        Assert.False(shouldAsk);
        Assert.True(shouldHandle);
    }

    [Fact]
    public void ProjectScaffoldPlanner_AsksForBareNewProject()
    {
        var root = CreateTempDirectory();

        var result = new ProjectScaffoldPlanner().Plan(
            "\uC5EC\uAE30\uC5D0 \uC0C8\uB85C\uC6B4 \uD504\uB85C\uC81D\uD2B8\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            root);

        Assert.True(result.IsGreenfieldRequest);
        Assert.False(result.CanProceed);
        Assert.Contains("\uC5B4\uB5A4 \uC885\uB958\uC758 \uD504\uB85C\uC81D\uD2B8", result.ClarifyingQuestion, StringComparison.Ordinal);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void ProjectScaffoldPlanner_PlansPortfolioAsJavaScriptByDefault()
    {
        var root = CreateTempDirectory();

        var result = new ProjectScaffoldPlanner().Plan(
            "\uC5EC\uAE30\uC5D0 \uD3EC\uD2B8\uD3F4\uB9AC\uC624 \uD648\uD398\uC774\uC9C0\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            root);

        Assert.True(result.IsGreenfieldRequest);
        Assert.True(result.CanProceed);
        Assert.Equal("portfolio", result.Intent?.ProjectType);
        Assert.Equal("javascript", result.Intent?.Language);
        Assert.Equal("vite-react", result.Intent?.Framework);
        Assert.Contains("src/main.jsx", result.Plan!.Files);
        Assert.DoesNotContain("src/main.tsx", result.Plan.Files);
        Assert.Contains("npm run build", result.Plan.VerificationCommands);
    }

    [Fact]
    public void ProjectScaffoldPlanner_HonorsExplicitTypeScriptPortfolio()
    {
        var root = CreateTempDirectory();

        var result = new ProjectScaffoldPlanner().Plan("Create a TypeScript portfolio website", root);

        Assert.True(result.CanProceed);
        Assert.Equal("typescript", result.Intent?.Language);
        Assert.Contains("src/main.tsx", result.Plan!.Files);
        Assert.DoesNotContain("src/main.jsx", result.Plan.Files);
    }

    [Fact]
    public void ProjectScaffoldPlanner_PlansPythonDataAnalysisTool()
    {
        var root = CreateTempDirectory();

        var result = new ProjectScaffoldPlanner().Plan(
            "\uD30C\uC774\uC36C\uC73C\uB85C \uAC04\uB2E8\uD55C \uB370\uC774\uD130 \uBD84\uC11D \uB3C4\uAD6C\uB97C \uB9CC\uB4E4\uC790",
            root);

        Assert.True(result.IsGreenfieldRequest);
        Assert.True(result.CanProceed);
        Assert.Equal("data-analysis-tool", result.Intent?.ProjectType);
        Assert.Equal("python", result.Intent?.Language);
        Assert.Contains("src/main.py", result.Plan!.Files);
        Assert.Contains("python -m pytest", result.Plan.VerificationCommands);
    }

    [Fact]
    public void ProjectScaffoldPlanner_AsksBeforeOverwritingExistingProject()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "README.md"), "existing");

        var result = new ProjectScaffoldPlanner().Plan(
            "\uC5EC\uAE30\uC5D0 \uC0C8\uB85C\uC6B4 \uD504\uB85C\uC81D\uD2B8\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            root);

        Assert.True(result.IsGreenfieldRequest);
        Assert.False(result.CanProceed);
        Assert.Contains("\uC774\uBBF8 \uD504\uB85C\uC81D\uD2B8 \uD30C\uC77C", result.ClarifyingQuestion, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectScaffoldPlanner_BuildsPlanContext()
    {
        var root = CreateTempDirectory();
        var result = new ProjectScaffoldPlanner().Plan("Create a portfolio website", root);

        var context = ProjectScaffoldPlanner.BuildPlanContext(result);

        Assert.Contains("Project scaffold preflight plan", context, StringComparison.Ordinal);
        Assert.Contains("projectType: portfolio", context, StringComparison.Ordinal);
        Assert.Contains("language: javascript", context, StringComparison.Ordinal);
        Assert.Contains("src/main.jsx", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesktopProjectScaffoldPlanTool_ReturnsProceedingPlan()
    {
        var root = CreateTempDirectory();
        var tool = new DesktopProjectScaffoldPlanTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["request"] = "\uADF8\uB7FC Python \uB370\uC774\uD130 \uBD84\uC11D \uB3C4\uAD6C\uB85C \uD558\uC790"
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var rootElement = document.RootElement;
        Assert.True(rootElement.GetProperty("isGreenfieldRequest").GetBoolean());
        Assert.True(rootElement.GetProperty("canProceed").GetBoolean());
        Assert.Equal("python", rootElement.GetProperty("intent").GetProperty("language").GetString());
        var files = rootElement.GetProperty("plan").GetProperty("files").EnumerateArray()
            .Select(file => file.GetString())
            .ToList();
        Assert.Contains("src/main.py", files);
        Assert.Contains("Project scaffold preflight plan", rootElement.GetProperty("planContext").GetString());
    }

    [Fact]
    public async Task DesktopProjectScaffoldPlanTool_ReturnsClarifyingQuestion()
    {
        var root = CreateTempDirectory();
        var tool = new DesktopProjectScaffoldPlanTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["request"] = "\uC0C8 \uD504\uB85C\uC81D\uD2B8 \uB9CC\uB4E4\uC790"
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var rootElement = document.RootElement;
        Assert.True(rootElement.GetProperty("isGreenfieldRequest").GetBoolean());
        Assert.False(rootElement.GetProperty("canProceed").GetBoolean());
        Assert.Contains("\uC5B4\uB5A4 \uC885\uB958\uC758 \uD504\uB85C\uC81D\uD2B8", rootElement.GetProperty("clarifyingQuestion").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesktopProjectScaffoldCreateTool_CreatesJavaScriptPortfolioFiles()
    {
        var root = CreateTempDirectory();
        var tool = new DesktopProjectScaffoldCreateTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["intent"] = new
            {
                projectType = "portfolio",
                language = "javascript",
                framework = "vite-react",
                style = "unspecified"
            },
            ["plan"] = new
            {
                name = "portfolio vite-react scaffold",
                files = new[] { "package.json", "index.html", "vite.config.js", "src/main.jsx", "src/App.jsx", "src/styles.css" },
                verificationCommands = new[] { "npm install", "npm run build" }
            }
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var rootElement = document.RootElement;
        Assert.True(rootElement.GetProperty("succeeded").GetBoolean());
        var created = rootElement.GetProperty("createdFiles").EnumerateArray()
            .Select(file => file.GetString())
            .ToList();
        Assert.Contains("package.json", created);
        Assert.Contains("src/main.jsx", created);
        Assert.Contains("src/App.jsx", created);
        Assert.True(File.Exists(Path.Combine(root, "package.json")));
        Assert.True(File.Exists(Path.Combine(root, "src", "main.jsx")));
        Assert.True(File.Exists(Path.Combine(root, "vite.config.js")));
        Assert.Contains("/src/main.jsx", File.ReadAllText(Path.Combine(root, "index.html")), StringComparison.Ordinal);
        Assert.Contains("defineConfig", File.ReadAllText(Path.Combine(root, "vite.config.js")), StringComparison.Ordinal);
        Assert.Contains("@vitejs/plugin-react", File.ReadAllText(Path.Combine(root, "vite.config.js")), StringComparison.Ordinal);
        Assert.Contains("import App from \"./App.jsx\"", File.ReadAllText(Path.Combine(root, "src", "main.jsx")), StringComparison.Ordinal);
        Assert.Contains("\"build\": \"vite build\"", File.ReadAllText(Path.Combine(root, "package.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("typescript", File.ReadAllText(Path.Combine(root, "package.json")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DesktopProjectScaffoldCreateTool_CreatesPythonDataAnalysisFilesWithMatchingImports()
    {
        var root = CreateTempDirectory();
        var tool = new DesktopProjectScaffoldCreateTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["intent"] = new
            {
                projectType = "data-analysis-tool",
                language = "python",
                framework = "python-cli",
                style = "unspecified"
            },
            ["plan"] = new
            {
                name = "Python data analysis CLI scaffold",
                files = new[] { "README.md", "requirements.txt", "src/main.py", "src/analyzer.py", "data/.gitkeep", "tests/test_analyzer.py" },
                verificationCommands = new[] { "python -m pytest" }
            }
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var rootElement = document.RootElement;
        Assert.True(rootElement.GetProperty("succeeded").GetBoolean());
        Assert.True(File.Exists(Path.Combine(root, "src", "analyzer.py")));
        Assert.True(File.Exists(Path.Combine(root, "src", "main.py")));
        Assert.True(File.Exists(Path.Combine(root, "tests", "test_analyzer.py")));
        Assert.Contains("from src.analyzer import create_data_analysis_tool_message", File.ReadAllText(Path.Combine(root, "src", "main.py")), StringComparison.Ordinal);
        Assert.Contains("from src.analyzer import create_data_analysis_tool_message", File.ReadAllText(Path.Combine(root, "tests", "test_analyzer.py")), StringComparison.Ordinal);
        Assert.Contains("pytest", File.ReadAllText(Path.Combine(root, "requirements.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesktopProjectScaffoldCreateTool_CreatesFastApiFilesWithMatchingImports()
    {
        var root = CreateTempDirectory();
        var tool = new DesktopProjectScaffoldCreateTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["intent"] = new
            {
                projectType = "api-server",
                language = "python",
                framework = "fastapi",
                style = "unspecified"
            },
            ["plan"] = new
            {
                name = "FastAPI service scaffold",
                files = new[] { "README.md", "requirements.txt", "app/main.py", "app/routes.py", "tests/test_app.py" },
                verificationCommands = new[] { "python -m pytest" }
            }
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var rootElement = document.RootElement;
        Assert.True(rootElement.GetProperty("succeeded").GetBoolean());
        Assert.Contains("from app.routes import router", File.ReadAllText(Path.Combine(root, "app", "main.py")), StringComparison.Ordinal);
        Assert.Contains("app = FastAPI()", File.ReadAllText(Path.Combine(root, "app", "main.py")), StringComparison.Ordinal);
        Assert.Contains("router = APIRouter", File.ReadAllText(Path.Combine(root, "app", "routes.py")), StringComparison.Ordinal);
        Assert.Contains("from app.main import app", File.ReadAllText(Path.Combine(root, "tests", "test_app.py")), StringComparison.Ordinal);
        Assert.Contains("fastapi", File.ReadAllText(Path.Combine(root, "requirements.txt")), StringComparison.Ordinal);
        Assert.Contains("httpx", File.ReadAllText(Path.Combine(root, "requirements.txt")), StringComparison.Ordinal);
        Assert.Contains("pytest", File.ReadAllText(Path.Combine(root, "requirements.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesktopProjectScaffoldCreateTool_DoesNotOverwriteExistingFilesByDefault()
    {
        var root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "package.json"), "{}");
        var tool = new DesktopProjectScaffoldCreateTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["intent"] = new
            {
                projectType = "portfolio",
                language = "javascript",
                framework = "vite-react",
                style = "unspecified"
            },
            ["plan"] = new
            {
                name = "portfolio vite-react scaffold",
                files = new[] { "package.json", "index.html", "vite.config.js", "src/main.jsx", "src/App.jsx", "src/styles.css" },
                verificationCommands = new[] { "npm install", "npm run build" }
            }
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var rootElement = document.RootElement;
        Assert.False(rootElement.GetProperty("succeeded").GetBoolean());
        var skipped = rootElement.GetProperty("skippedFiles").EnumerateArray()
            .Select(file => file.GetString())
            .ToList();
        Assert.Contains("package.json", skipped);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(root, "package.json")));
        Assert.Contains("target files already exist", string.Join(" ", rootElement.GetProperty("issues").EnumerateArray().Select(issue => issue.GetString())), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesktopProjectScaffoldCreateTool_RejectsRequestWithoutApprovedPlan()
    {
        var root = CreateTempDirectory();
        var tool = new DesktopProjectScaffoldCreateTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["request"] = "Create a portfolio website"
        });

        Assert.True(result.IsError);
        Assert.Contains("Call plan_project_scaffold first", result.ErrorMessage, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "package.json")));
    }

    [Fact]
    public async Task DesktopProjectScaffoldCreateTool_RejectsPlanThatEscapesWorkspace()
    {
        var root = CreateTempDirectory();
        var tool = new DesktopProjectScaffoldCreateTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["intent"] = new
            {
                projectType = "portfolio",
                language = "javascript",
                framework = "vite-react",
                style = "unspecified"
            },
            ["plan"] = new
            {
                name = "unsafe scaffold",
                files = new[] { "../outside.txt" },
                verificationCommands = Array.Empty<string>()
            }
        });

        Assert.True(result.IsError);
        Assert.Contains("escapes the workspace", result.ErrorMessage, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Directory.GetParent(root)!.FullName, "outside.txt")));
    }

    [Fact]
    public void ToolPermissionClassifier_ClassifiesProjectScaffoldCreationAsProjectWrite()
    {
        var assessment = ToolPermissionClassifier.Assess(
            "create_project_scaffold",
            new Dictionary<string, object?>
            {
                ["intent"] = new
                {
                    projectType = "portfolio",
                    language = "javascript",
                    framework = "vite-react",
                    style = "unspecified"
                },
                ["plan"] = new
                {
                    name = "portfolio vite-react scaffold",
                    files = new[] { "package.json", "index.html", "vite.config.js", "src/main.jsx", "src/App.jsx", "src/styles.css" },
                    verificationCommands = new[] { "npm install", "npm run build" }
                }
            });

        var result = ToolPermissionPolicy.Evaluate(assessment, AgentWorkMode.Coding);

        Assert.Equal(PermissionRiskLevel.ProjectWrite, assessment.RiskLevel);
        Assert.Contains("package.json", assessment.Target, StringComparison.Ordinal);
        Assert.Contains("npm run build", assessment.Reason, StringComparison.Ordinal);
        Assert.Equal(ToolPermissionDecision.RequireApproval, result.Decision);
        Assert.False(result.IsBlocked);
    }

    [Fact]
    public void ToolPermissionClassifier_IncludesProjectScaffoldExistingFileCollisions()
    {
        var root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "package.json"), "{}");

        var assessment = ToolPermissionClassifier.Assess(
            "create_project_scaffold",
            new Dictionary<string, object?>
            {
                ["intent"] = new
                {
                    projectType = "portfolio",
                    language = "javascript",
                    framework = "vite-react",
                    style = "unspecified"
                },
                ["plan"] = new
                {
                    name = "portfolio vite-react scaffold",
                    files = new[] { "package.json", "index.html", "vite.config.js" },
                    verificationCommands = new[] { "npm install", "npm run build" }
                }
            },
            root);

        Assert.Equal(PermissionRiskLevel.ProjectWrite, assessment.RiskLevel);
        Assert.Contains("existing target-file collisions: package.json", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesktopProjectScaffoldVerifyTool_RunsApprovedPlanCommand()
    {
        var root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "test.cmd"), "@echo off\r\necho scaffold ok\r\nexit /b 0\r\n");
        var tool = new DesktopProjectScaffoldVerifyTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["plan"] = new
            {
                name = "test scaffold",
                files = new[] { "test.cmd" },
                verificationCommands = new[] { "cmd /c test.cmd" }
            }
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var rootElement = document.RootElement;
        Assert.True(rootElement.GetProperty("succeeded").GetBoolean());
        Assert.Equal("cmd /c test.cmd", rootElement.GetProperty("command").GetString());
        Assert.Equal(0, rootElement.GetProperty("exitCode").GetInt32());
        Assert.Contains("scaffold ok", rootElement.GetProperty("combinedOutput").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DesktopProjectScaffoldVerifyTool_RejectsCommandOutsideApprovedPlan()
    {
        var root = CreateTempDirectory();
        var tool = new DesktopProjectScaffoldVerifyTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["plan"] = new
            {
                name = "test scaffold",
                files = Array.Empty<string>(),
                verificationCommands = new[] { "npm run build" }
            },
            ["command"] = "npm test"
        });

        Assert.True(result.IsError);
        Assert.Contains("not part of the approved", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesktopProjectScaffoldVerifyTool_RejectsUnsafeCommandEvenWhenListedInPlan()
    {
        var root = CreateTempDirectory();
        var tool = new DesktopProjectScaffoldVerifyTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["plan"] = new
            {
                name = "unsafe scaffold",
                files = Array.Empty<string>(),
                verificationCommands = new[] { "Remove-Item -Recurse . -Force" }
            }
        });

        Assert.True(result.IsError);
        Assert.Contains("not allowed by the verification command policy", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesktopProjectScaffoldVerifyTool_ReturnsRepairContextForFailedVerification()
    {
        var root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "test.cmd"), "@echo off\r\necho scaffold failed\r\nexit /b 1\r\n");
        var tool = new DesktopProjectScaffoldVerifyTool(root);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["plan"] = new
            {
                name = "test scaffold",
                files = new[] { "test.cmd" },
                verificationCommands = new[] { "cmd /c test.cmd" }
            }
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var rootElement = document.RootElement;
        Assert.False(rootElement.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1, rootElement.GetProperty("exitCode").GetInt32());
        Assert.Contains("scaffold failed", rootElement.GetProperty("combinedOutput").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Tests failed", rootElement.GetProperty("failureAnalysis").GetProperty("title").GetString());
        Assert.Contains("The last verification command failed", rootElement.GetProperty("repairPrompt").GetString(), StringComparison.Ordinal);
        Assert.Contains("Repair failed project scaffold verification", rootElement.GetProperty("repairPlan").GetProperty("goal").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToolPermissionClassifier_ClassifiesProjectScaffoldVerificationAsVerificationCommand()
    {
        var assessment = ToolPermissionClassifier.Assess(
            "verify_project_scaffold",
            new Dictionary<string, object?>
            {
                ["plan"] = new
                {
                    name = "portfolio vite-react scaffold",
                    files = new[] { "package.json" },
                    verificationCommands = new[] { "npm install", "npm run build" }
                },
                ["command"] = "npm run build"
            });

        var result = ToolPermissionPolicy.Evaluate(assessment, AgentWorkMode.Coding);

        Assert.Equal(PermissionRiskLevel.VerificationCommand, assessment.RiskLevel);
        Assert.Equal("npm run build", assessment.Target);
        Assert.Contains("plan allows", assessment.Reason, StringComparison.Ordinal);
        Assert.Equal(ToolPermissionDecision.RequireApproval, result.Decision);
        Assert.False(result.IsBlocked);
    }

    [Fact]
    public async Task WorkspaceAnalysisService_UsesTypeScriptWorkerResults()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "frontend", "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "package.json"),
            """
            {
              "dependencies": { "react": "latest" },
              "devDependencies": { "vite": "latest", "typescript": "latest", "@playwright/test": "latest" },
              "scripts": { "build": "vite build", "test:e2e": "playwright test" }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "playwright.config.ts"),
            "export default {};");
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "tsconfig.json"),
            """{"compilerOptions":{"baseUrl":".","paths":{"@/*":["src/*"]}}}""");
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "src", "api.ts"),
            "export const apiClient = {};");
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "src", "App.tsx"),
            """
            import { apiClient } from '@/api';

            export const useAppData = () => apiClient;

            export function AppShell() {
              return apiClient;
            }
            """);
        Directory.CreateDirectory(Path.Combine(root, "frontend", "src", "app", "api", "status"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "src", "app", "api", "status", "route.ts"),
            "export async function POST() { return Response.json({ ok: true }); }");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains("React", analysis.Framework);
        Assert.Contains("Vite", analysis.Framework);
        Assert.Contains("TypeScript", analysis.Framework);
        Assert.Contains("Playwright", analysis.Framework);
        Assert.Contains(analysis.Hints, hint => hint.Contains("JavaScript/TypeScript worker indexed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Hints, hint => hint.Contains("Playwright detected", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeyFiles, file => file == "frontend/playwright.config.ts");
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("Playwright config", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.VerificationCommands, command => command.Contains("cd frontend", StringComparison.OrdinalIgnoreCase) &&
                                                                  command.Contains("npm run test:e2e", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("component AppShell", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("hook useAppData", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("api POST /api/status", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Hints, hint => hint.Contains("JavaScript/TypeScript worker capability: create-react-feature", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeyDependencies, dependency => dependency.Contains("frontend/src/App.tsx", StringComparison.OrdinalIgnoreCase) &&
                                                                dependency.Contains("frontend/src/api.ts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PythonWorkerHost_ExtractsFastApiSqlAlchemyAndPytestSignals()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "backend", "app"));
        Directory.CreateDirectory(Path.Combine(root, "backend", "tests"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "requirements.txt"),
            """
            fastapi==0.111.0
            sqlalchemy==2.0.0
            pytest==8.0.0
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "app", "__init__.py"),
            "");
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "app", "models.py"),
            """
            from sqlalchemy.orm import DeclarativeBase

            class Base(DeclarativeBase):
                pass

            class User(Base):
                __tablename__ = "users"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "app", "main.py"),
            """
            from fastapi import FastAPI
            from .models import User

            app = FastAPI()

            @app.get("/users")
            async def list_users():
                return [User()]
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "app", "views.py"),
            """
            import click
            from celery import shared_task
            from flask import Flask

            app = Flask(__name__)

            @app.route("/health", methods=["GET", "POST"])
            def health():
                return "ok"

            @shared_task
            def rebuild_cache():
                return True

            @click.command("sync-users")
            def sync_users():
                return None
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "tests", "test_users.py"),
            """
            import pytest

            @pytest.fixture
            def user_id():
                return 1

            def test_users():
                assert True
            """);

        var result = await new PythonWorkerHost().AnalyzeAsync(root, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Requirements, item => item.Path == "backend/requirements.txt");
        Assert.Contains(result.Imports, item => item.Module == "fastapi");
        Assert.Contains(result.Imports, item => item.Module == "models" &&
                                                item.ResolvedPath == "backend/app/models.py");
        Assert.Contains(result.CallSites, item => item.Name == "User" &&
                                                  item.EnclosingSymbol == "list_users");
        Assert.Contains(result.FastApiRoutes, route => route.Route == "/users" && route.Method == "GET");
        Assert.Contains(result.WebRoutes, route => route.Framework == "Flask" &&
                                                  route.Route == "/health" &&
                                                  route.Method == "GET,POST");
        Assert.Contains(result.CeleryTasks, task => task.Name == "rebuild_cache");
        Assert.Contains(result.CliCommands, command => command.Command == "sync-users");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-fastapi-feature");
        Assert.Contains(result.Capabilities, capability => capability.Name == "create-flask-blueprint");
        Assert.Contains(result.ScaffoldRecommendations, recommendation => recommendation.Name == "FastAPI service feature");
        Assert.Contains(result.SqlAlchemyModels, model => model.Name == "User");
        Assert.Contains(result.PytestTargets, target => target.Path == "backend/tests/test_users.py" &&
                                                       target.Kind == "test-file");
        Assert.Contains(result.PytestTargets, target => target.Name == "test_users" &&
                                                       target.Kind == "test-function");
        Assert.Contains(result.Symbols, symbol => symbol.Name == "list_users");
    }

    [Fact]
    public async Task WorkspaceAnalysisService_UsesPythonWorkerResults()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "backend", "app"));
        Directory.CreateDirectory(Path.Combine(root, "backend", "tests"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "requirements.txt"),
            """
            fastapi
            sqlalchemy
            pytest
            flask
            celery
            click
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "app", "__init__.py"),
            "");
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "app", "models.py"),
            """
            class User:
                __tablename__ = "users"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "app", "main.py"),
            """
            from fastapi import FastAPI
            from app.models import User

            app = FastAPI()

            @app.post("/users")
            def create_user():
                return User()
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "app", "tasks.py"),
            """
            from celery import shared_task
            from flask import Flask
            import click

            app = Flask(__name__)

            @app.route("/health")
            def health():
                return "ok"

            @shared_task
            def rebuild_cache():
                return True

            @click.command()
            def sync_users():
                return None
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "backend", "tests", "test_api.py"), "def test_api(): assert True");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains("FastAPI", analysis.Framework);
        Assert.Contains("Flask", analysis.Framework);
        Assert.Contains("Celery", analysis.Framework);
        Assert.Contains("SQLAlchemy", analysis.Framework);
        Assert.Contains("pytest", analysis.Framework);
        Assert.Contains(analysis.Hints, hint => hint.Contains("Python worker indexed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("route POST /users", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("route Flask GET /health", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("model User", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("celery task rebuild_cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("cli sync-users", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Hints, hint => hint.Contains("Python worker capability: create-fastapi-feature", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("call User", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeyDependencies, dependency => dependency.Contains("backend/app/main.py", StringComparison.OrdinalIgnoreCase) &&
                                                                dependency.Contains("backend/app/models.py", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ProjectMap, entry => entry.Contains("FastAPI routes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.VerificationCommands, command => command.Contains("python -m pytest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DesktopEvidenceFormatter_ExplainsReadFilePathRole()
    {
        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "read_file",
            new Dictionary<string, object?> { ["path"] = "src/components/LoginView.xaml" },
            "C:\\repo");

        Assert.Contains("Read file: src/components/LoginView.xaml", evidence);
        Assert.Contains("UI layer", evidence);
    }

    [Fact]
    public void DesktopEvidenceFormatter_ExplainsKeyProjectFile()
    {
        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "read_file",
            new Dictionary<string, object?> { ["path"] = "README.md" },
            "C:\\repo");

        Assert.Contains("key project file", evidence);
    }

    [Fact]
    public async Task DesktopEvidenceFormatter_ExplainsCSharpSymbolsInReadFile()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "Services"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "Services", "AuthService.cs"),
            """
            public sealed class AuthService
            {
                public bool Login(string email) => true;
            }
            """);

        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "read_file",
            new Dictionary<string, object?> { ["path"] = Path.Combine("Services", "AuthService.cs") },
            root);

        Assert.Contains("Contains symbols", evidence);
        Assert.Contains("class AuthService", evidence);
        Assert.Contains("method AuthService.Login", evidence);
    }

    [Fact]
    public async Task WorkspaceIndexer_PrioritizesFilesMatchingQueryTerms()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Project");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "BillingReport.cs"), "public sealed class BillingReport {}");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "AuthLoginService.cs"), "public sealed class AuthLoginService {}");

        var context = await new WorkspaceIndexer().BuildContextAsync(root, "fix auth login failure", CancellationToken.None);

        Assert.Contains("Query-aware priority terms: fix, auth, login, failure", context);
        Assert.True(
            context.IndexOf("--- src/AuthLoginService.cs ---", StringComparison.Ordinal) <
            context.IndexOf("--- README.md ---", StringComparison.Ordinal));
        Assert.True(
            context.IndexOf("--- src/AuthLoginService.cs ---", StringComparison.Ordinal) <
            context.IndexOf("--- src/BillingReport.cs ---", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkspaceIndexer_TreatsAgentQOnlyWorkspaceAsGreenfieldBootstrap()
    {
        var root = CreateTempDirectory();
        var replayDirectory = Path.Combine(root, ".agentq", "replay");
        Directory.CreateDirectory(replayDirectory);
        await File.WriteAllTextAsync(Path.Combine(replayDirectory, "run.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(root, ".agentq", "events.jsonl"), "{}");

        var context = await new WorkspaceIndexer().BuildContextAsync(root, "Build a portfolio website", CancellationToken.None);

        Assert.Contains("No user project files were found", context);
        Assert.Contains("Empty-workspace bootstrap guidance", context);
        Assert.Contains("Vite + React + JavaScript", context);
        Assert.Contains("JavaScript is requested after TypeScript was recommended", context);
        Assert.Contains("Use TypeScript", context);
        Assert.DoesNotContain(".agentq", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceIndexer_AsksDirectionForBareNewProjectRequest()
    {
        var root = CreateTempDirectory();

        var context = await new WorkspaceIndexer().BuildContextAsync(
            root,
            "\uC5EC\uAE30\uC5D0 \uC0C8\uB85C\uC6B4 \uD504\uB85C\uC81D\uD2B8\uB97C \uB9CC\uB4E4\uACE0 \uC2F6\uB2E4",
            CancellationToken.None);

        Assert.Contains("ask what kind of project", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before choosing a stack", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not say you will create a specific starter", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DesktopEvidenceFormatter_ExplainsPythonSymbolsInReadFile()
    {
        var root = CreateTempDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(root, "worker.py"),
            """
            class JobRunner:
                def run(self):
                    return True
            """);

        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "read_file",
            new Dictionary<string, object?> { ["path"] = "worker.py" },
            root);

        Assert.Contains("Contains symbols", evidence);
        Assert.Contains("class JobRunner", evidence);
        Assert.Contains("function JobRunner.run", evidence);
    }

    [Fact]
    public async Task DesktopEvidenceFormatter_ExplainsDependencyGraphNeighbors()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "auth.ts"), "export const login = () => true;");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "LoginPage.tsx"), "import { login } from './auth';");

        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "read_file",
            new Dictionary<string, object?> { ["path"] = "src/auth.ts" },
            root);

        Assert.Contains("Graph:", evidence);
        Assert.Contains("imported by src/LoginPage.tsx", evidence);
    }

    [Fact]
    public async Task DesktopEvidenceFormatter_ExplainsLocalMemoryMentions()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, ".agentq"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "auth.ts"), "export const login = () => true;");
        await File.WriteAllTextAsync(
            Path.Combine(root, ".agentq", "memory.local.json"),
            """
            {
              "version": 1,
              "lessons": [
                {
                  "id": "auth-location",
                  "title": "Auth logic lives in auth.ts",
                  "content": "Use src/auth.ts for login behavior.",
                  "enabled": true
                }
              ]
            }
            """);

        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "read_file",
            new Dictionary<string, object?> { ["path"] = "src/auth.ts" },
            root);

        Assert.Contains("Memory:", evidence);
        Assert.Contains("Auth logic lives in auth.ts", evidence);
    }

    [Fact]
    public void DesktopEvidenceFormatter_ExplainsBroadSearch()
    {
        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "grep_search",
            new Dictionary<string, object?> { ["pattern"] = "ProjectMap" },
            "C:\\repo");

        Assert.Contains("broad workspace search", evidence);
    }

    [Fact]
    public void DesktopEvidenceFormatter_ExplainsSemanticSearch()
    {
        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "semantic_search",
            new Dictionary<string, object?> { ["query"] = "login flow" },
            "C:\\repo");

        Assert.Contains("Semantic search query: login flow", evidence);
        Assert.Contains("meaning-based lookup", evidence);
    }

    [Fact]
    public void DesktopEvidenceFormatter_ExplainsSymbolSearch()
    {
        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "symbol_search",
            new Dictionary<string, object?> { ["query"] = "LoginAsync" },
            "C:\\repo");

        Assert.Contains("Symbol search query: LoginAsync", evidence);
        Assert.Contains("symbol index lookup", evidence);
    }

    [Fact]
    public void DesktopEvidenceFormatter_ExplainsHybridSearch()
    {
        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "hybrid_search",
            new Dictionary<string, object?> { ["query"] = "login flow" },
            "C:\\repo");

        Assert.Contains("Hybrid search query: login flow", evidence);
        Assert.Contains("combined symbol", evidence);
        Assert.Contains("dependency graph", evidence);
        Assert.Contains("Git recency", evidence);
        Assert.Contains("project memory", evidence);
    }

    [Fact]
    public async Task LinkContentFetcher_ReportsAutoReadSuccessAsEvidence()
    {
        using var factory = new StubHttpClientFactory(
            "<html><body><h1>Hello link</h1><script>ignore()</script><p>Readable page text.</p></body></html>",
            contentType: "text/html");
        var context = await new LinkContentFetcher(factory).BuildContextAsync("please read https://example.test/page", CancellationToken.None);

        Assert.Contains("Link auto-read is enabled", context);
        Assert.Contains("URL: https://example.test/page", context);
        Assert.Contains("Fetch succeeded", context);
        Assert.Contains("Hello link", context);
        Assert.DoesNotContain("ignore()", context);
    }

    [Fact]
    public async Task LinkContentFetcher_ReturnsStructuredSuccessResult()
    {
        using var factory = new StubHttpClientFactory(
            "<html><body><h1>Hello link</h1><p>Readable page text.</p></body></html>",
            contentType: "text/html");
        var results = await new LinkContentFetcher(factory).FetchAsync("please read https://example.test/page", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.Succeeded);
        Assert.Equal(LinkFetchStatus.Succeeded, result.Status);
        Assert.Equal("https://example.test/page", result.Url);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal("text/html", result.ContentType);
        Assert.Contains("Hello link", result.Excerpt);
    }

    [Fact]
    public async Task LinkContentFetcher_ReportsHttpFailureReason()
    {
        using var factory = new StubHttpClientFactory("forbidden", HttpStatusCode.Forbidden);
        var context = await new LinkContentFetcher(factory).BuildContextAsync("please read https://example.test/private", CancellationToken.None);

        Assert.Contains("Link auto-read is enabled", context);
        Assert.Contains("URL: https://example.test/private", context);
        Assert.Contains("Fetch failed: HTTP 403", context);
        Assert.Contains("failure reason", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LinkContentFetcher_ReturnsStructuredHttpFailureResult()
    {
        using var factory = new StubHttpClientFactory("forbidden", HttpStatusCode.Forbidden);
        var results = await new LinkContentFetcher(factory).FetchAsync("please read https://example.test/private", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.Succeeded);
        Assert.Equal(LinkFetchStatus.HttpError, result.Status);
        Assert.Equal(403, result.HttpStatusCode);
        Assert.Contains("403", result.FailureReason);
    }

    [Fact]
    public async Task LinkContentFetcher_ReturnsUnsupportedContentTypeResult()
    {
        using var factory = new StubHttpClientFactory("binary", contentType: "application/octet-stream");
        var results = await new LinkContentFetcher(factory).FetchAsync("please read https://example.test/file", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(LinkFetchStatus.UnsupportedContentType, result.Status);
        Assert.Equal("application/octet-stream", result.ContentType);
        Assert.Contains("unsupported content type", result.FailureReason);
    }

    [Fact]
    public void DesktopConfidenceAssessor_RatesVerifiedToolBackedRunHigh()
    {
        var assessment = DesktopConfidenceAssessor.Assess(
            "Done",
            toolCallCount: 3,
            fileChanges:
            [
                new FileChangeRecord
                {
                    Path = "C:\\repo\\csharp\\AgentQ.Desktop\\Services\\Test.cs",
                    RelativePath = "csharp/AgentQ.Desktop/Services/Test.cs",
                    DiffLines = [new DiffLine { Kind = DiffLineKind.Added, Text = "changed" }]
                }
            ],
            executedCommands: ["dotnet test .\\csharp\\AgentQ.sln -c Release"],
            verificationPlans:
            [
                new AgentVerificationPlan
                {
                    Title = "Verification already ran",
                    AlreadySatisfied = true
                }
            ],
            touchedMemoryCount: 1);

        Assert.Equal("High", assessment.Level);
        Assert.Contains(assessment.Signals, signal => signal.Contains("verification", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(assessment.Warnings);
    }

    [Fact]
    public void DesktopConfidenceAssessor_WarnsWhenChangesAreUnverified()
    {
        var assessment = DesktopConfidenceAssessor.Assess(
            "Changed the file",
            toolCallCount: 1,
            fileChanges:
            [
                new FileChangeRecord
                {
                    Path = "C:\\repo\\csharp\\AgentQ.Desktop\\Services\\Test.cs",
                    RelativePath = "csharp/AgentQ.Desktop/Services/Test.cs",
                    DiffLines = [new DiffLine { Kind = DiffLineKind.Added, Text = "changed" }]
                }
            ],
            executedCommands: [],
            verificationPlans:
            [
                new AgentVerificationPlan
                {
                    Title = "Suggested verification",
                    Command = "dotnet build csharp\\AgentQ.Desktop\\AgentQ.Desktop.csproj"
                }
            ],
            touchedMemoryCount: 0);

        Assert.Equal("Low", assessment.Level);
        Assert.Contains(assessment.Warnings, warning => warning.Contains("without a completed build/test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DesktopConfidenceAssessor_RewardsGraphMemoryAndGitSearchEvidence()
    {
        var assessment = DesktopConfidenceAssessor.Assess(
            "Done",
            toolCallCount: 2,
            fileChanges: [],
            executedCommands: [],
            verificationPlans: [],
            touchedMemoryCount: 1,
            toolEvidence:
            [
                new ToolReplayEntry
                {
                    ToolName = "hybrid_search",
                    ResultPreview = """
                    {"results":[{"Sources":["symbol","graph","memory","git"],"Reasons":["graph: imports candidate src/auth.ts"]}]}
                    """
                },
                new ToolReplayEntry
                {
                    ToolName = "read_file",
                    ResultPreview = "src/auth.ts"
                }
            ]);

        Assert.Contains(assessment.Signals, signal => signal.Contains("search/navigation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Signals, signal => signal.Contains("file read", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Signals, signal => signal.Contains("dependency graph", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Signals, signal => signal.Contains("project memory evidence", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Signals, signal => signal.Contains("Git recency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DesktopConfidenceAssessor_WarnsWhenChangedFilesLackContextEvidence()
    {
        var assessment = DesktopConfidenceAssessor.Assess(
            "Changed the file",
            toolCallCount: 1,
            fileChanges:
            [
                new FileChangeRecord
                {
                    Path = "C:\\repo\\src\\auth.ts",
                    RelativePath = "src/auth.ts",
                    DiffLines = [new DiffLine { Kind = DiffLineKind.Added, Text = "changed" }]
                }
            ],
            executedCommands: [],
            verificationPlans: [],
            touchedMemoryCount: 0,
            toolEvidence:
            [
                new ToolReplayEntry
                {
                    ToolName = "edit_file",
                    ResultPreview = "changed src/auth.ts"
                }
            ]);

        Assert.Contains(assessment.Warnings, warning => warning.Contains("without reading file context", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Warnings, warning => warning.Contains("without search or symbol navigation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DesktopVerificationSelector_SelectsFrontendBuildForTypeScriptChanges()
    {
        var plans = DesktopVerificationSelector.SelectPlans(
            [
                new FileChangeRecord
                {
                    Path = "C:\\repo\\frontend\\src\\App.tsx",
                    RelativePath = "frontend/src/App.tsx"
                }
            ],
            executedCommands: []);

        var plan = Assert.Single(plans);
        Assert.Equal("Focused verification", plan.Title);
        Assert.Equal("cmd /c cd frontend && npm run build", plan.Command);
        Assert.True(VerificationCommandPolicy.IsAllowed(plan.Command));
    }

    [Fact]
    public void DesktopVerificationSelector_SelectsBackendPytestForPythonChanges()
    {
        var plans = DesktopVerificationSelector.SelectPlans(
            [
                new FileChangeRecord
                {
                    Path = "C:\\repo\\backend\\app\\main.py",
                    RelativePath = "backend/app/main.py"
                }
            ],
            executedCommands: []);

        var plan = Assert.Single(plans);
        Assert.Equal("Focused verification", plan.Title);
        Assert.Equal("cmd /c cd backend && python -m pytest", plan.Command);
        Assert.True(VerificationCommandPolicy.IsAllowed(plan.Command));
    }

    [Fact]
    public void DesktopVerificationSelector_SelectsDockerComposeConfigForComposeChanges()
    {
        var plans = DesktopVerificationSelector.SelectPlans(
            [
                new FileChangeRecord
                {
                    Path = "C:\\repo\\docker-compose.yml",
                    RelativePath = "docker-compose.yml"
                }
            ],
            executedCommands: []);

        var plan = Assert.Single(plans);
        Assert.Equal("docker compose config", plan.Command);
        Assert.True(VerificationCommandPolicy.IsAllowed(plan.Command));
    }

    [Fact]
    public void DesktopVerificationSelector_SelectsFocusedDotnetTestForCSharpTestChanges()
    {
        var plans = DesktopVerificationSelector.SelectPlans(
            [
                new FileChangeRecord
                {
                    Path = "C:\\repo\\csharp\\AgentQ.Tests\\DesktopServiceTests.cs",
                    RelativePath = "csharp/AgentQ.Tests/DesktopServiceTests.cs"
                }
            ],
            executedCommands: []);

        var plan = Assert.Single(plans);
        Assert.Equal("Focused verification", plan.Title);
        Assert.Equal(
            "dotnet test csharp\\AgentQ.Tests\\AgentQ.Tests.csproj --filter FullyQualifiedName~DesktopServiceTests",
            plan.Command);
        Assert.True(VerificationCommandPolicy.IsAllowed(plan.Command));
    }

    [Fact]
    public void VerificationCommandPolicy_BlocksUnsafeDirectoryScopedCommands()
    {
        Assert.False(VerificationCommandPolicy.IsAllowed("cmd /c cd .. && npm run build"));
        Assert.False(VerificationCommandPolicy.IsAllowed("cmd /c cd frontend & del * && npm run build"));
        Assert.False(VerificationCommandPolicy.IsAllowed("dotnet test csharp\\AgentQ.Tests\\AgentQ.Tests.csproj --filter FullyQualifiedName~DesktopServiceTests;Remove-Item"));
    }

    [Fact]
    public void VerificationCommandPolicy_AllowsPlaywrightCommands()
    {
        Assert.True(VerificationCommandPolicy.IsAllowed("npx playwright test"));
        Assert.True(VerificationCommandPolicy.IsAllowed("npm run test:e2e"));
        Assert.True(VerificationCommandPolicy.IsAllowed("cmd /c cd frontend && npm run test:e2e"));
        Assert.True(VerificationCommandPolicy.IsAllowed("cmd /c cd frontend && npx playwright test"));
    }

    [Fact]
    public void PlaywrightVerificationArtifactCollector_FindsReportsAndScreenshots()
    {
        var root = CreateTempDirectory();
        var screenshotDirectory = Path.Combine(root, "frontend", "test-results", "login-chromium");
        Directory.CreateDirectory(screenshotDirectory);
        File.WriteAllBytes(Path.Combine(screenshotDirectory, "failure.png"), [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(root, "frontend", "playwright-report"));

        var artifacts = new PlaywrightVerificationArtifactCollector().Collect(
            new AgentVerificationPlan
            {
                Title = "E2E",
                Command = "cmd /c cd frontend && npm run test:e2e",
                Reason = "Run Playwright checks."
            },
            new VerificationRunResult
            {
                ExitCode = 1,
                StandardOutput = "Running playwright test"
            },
            root);

        Assert.Contains(artifacts, artifact => artifact.Kind == "playwright-report" &&
                                              artifact.Path == "frontend/playwright-report");
        Assert.Contains(artifacts, artifact => artifact.Kind == "screenshot" &&
                                              artifact.Path == "frontend/test-results/login-chromium/failure.png");
    }

    [Fact]
    public void PlaywrightVerificationArtifactCollector_IgnoresNonPlaywrightRuns()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "test-results"));
        File.WriteAllBytes(Path.Combine(root, "test-results", "failure.png"), [1, 2, 3]);

        var artifacts = new PlaywrightVerificationArtifactCollector().Collect(
            new AgentVerificationPlan
            {
                Title = "Unit tests",
                Command = "npm test",
                Reason = "Run unit tests."
            },
            new VerificationRunResult
            {
                ExitCode = 1,
                StandardOutput = "failed"
            },
            root);

        Assert.Empty(artifacts);
    }

    [Fact]
    public void VerificationArtifactEvidenceBuilder_SummarizesArtifacts()
    {
        var summary = new VerificationArtifactEvidenceBuilder().BuildSummary(
            [
                new VerificationArtifact
                {
                    Kind = "screenshot",
                    Path = "frontend/test-results/login/failure.png",
                    Description = "Playwright screenshot evidence."
                },
                new VerificationArtifact
                {
                    Kind = "playwright-report",
                    Path = "frontend/playwright-report",
                    Description = "Playwright HTML report directory."
                }
            ]);

        Assert.Contains("Artifact screenshot", summary);
        Assert.Contains("frontend/test-results/login/failure.png", summary);
        Assert.Contains("Artifact playwright-report", summary);
    }

    [Fact]
    public void ScreenshotEvidenceQualityChecker_FlagsSmallMissingAndDuplicateScreenshots()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "test-results"));
        File.WriteAllBytes(Path.Combine(root, "test-results", "small.png"), [1, 2, 3]);
        var largeBytes = Enumerable.Range(0, 1024).Select(index => (byte)(index % 255)).ToArray();
        File.WriteAllBytes(Path.Combine(root, "test-results", "a.png"), largeBytes);
        File.WriteAllBytes(Path.Combine(root, "test-results", "b.png"), largeBytes);

        var results = new ScreenshotEvidenceQualityChecker().Check(
            [
                new VerificationArtifact { Kind = "screenshot", Path = "test-results/small.png" },
                new VerificationArtifact { Kind = "screenshot", Path = "test-results/missing.png" },
                new VerificationArtifact { Kind = "screenshot", Path = "test-results/a.png" },
                new VerificationArtifact { Kind = "screenshot", Path = "test-results/b.png" },
                new VerificationArtifact { Kind = "screenshot", Path = "test-results/raw.bmp" }
            ],
            root);

        Assert.Contains(results, result => result.Path == "test-results/small.png" &&
                                           result.Status == ScreenshotEvidenceQualityStatus.TooSmall);
        Assert.Contains(results, result => result.Path == "test-results/missing.png" &&
                                           result.Status == ScreenshotEvidenceQualityStatus.Missing);
        Assert.Contains(results, result => result.Path == "test-results/a.png" &&
                                           result.Status == ScreenshotEvidenceQualityStatus.Valid);
        Assert.Contains(results, result => result.Path == "test-results/b.png" &&
                                           result.Status == ScreenshotEvidenceQualityStatus.Duplicate);
        Assert.Contains(results, result => result.Path == "test-results/raw.bmp" &&
                                           result.Status == ScreenshotEvidenceQualityStatus.UnsupportedExtension);
    }

    [Fact]
    public void VerificationArtifactEvidenceBuilder_IncludesScreenshotQualityEvidence()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "test-results"));
        File.WriteAllBytes(Path.Combine(root, "test-results", "failure.png"), [1, 2, 3]);

        var evidence = new VerificationArtifactEvidenceBuilder().BuildEvidence(
            [
                new VerificationArtifact
                {
                    Kind = "screenshot",
                    Path = "test-results/failure.png",
                    Description = "Playwright screenshot evidence."
                }
            ],
            root);

        Assert.Contains(evidence, item => item.Contains("Artifact screenshot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(evidence, item => item.Contains("Screenshot quality TooSmall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScreenshotVisualReviewService_QueuesOnlyValidScreenshots()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "test-results"));
        var largeBytes = Enumerable.Range(0, 1024).Select(index => (byte)(index % 255)).ToArray();
        File.WriteAllBytes(Path.Combine(root, "test-results", "valid.png"), largeBytes);
        File.WriteAllBytes(Path.Combine(root, "test-results", "tiny.png"), [1, 2, 3]);

        var candidates = new ScreenshotVisualReviewService().SelectCandidates(
            [
                new VerificationArtifact { Kind = "screenshot", Path = "test-results/valid.png" },
                new VerificationArtifact { Kind = "screenshot", Path = "test-results/tiny.png" },
                new VerificationArtifact { Kind = "playwright-report", Path = "playwright-report" }
            ],
            root);

        var candidate = Assert.Single(candidates);
        Assert.Equal("test-results/valid.png", candidate.RelativePath);
        Assert.True(Path.IsPathRooted(candidate.FullPath));
        Assert.Contains("reviewed", candidate.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScreenshotVisualHeuristicEvaluator_FlagsDarkScreensAndPassesVariedScreenshots()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "test-results"));
        var darkPath = Path.Combine(root, "test-results", "dark.png");
        var variedPath = Path.Combine(root, "test-results", "varied.png");
        SaveTestPng(darkPath, 32, 32, (_, _) => (0, 0, 0));
        SaveTestPng(variedPath, 32, 32, (x, y) => ((byte)(x * 7), (byte)(y * 7), (byte)((x + y) * 3)));

        var evaluator = new ScreenshotVisualHeuristicEvaluator();
        var dark = evaluator.Evaluate(new ScreenshotVisualReviewCandidate
        {
            RelativePath = "test-results/dark.png",
            FullPath = darkPath
        });
        var varied = evaluator.Evaluate(new ScreenshotVisualReviewCandidate
        {
            RelativePath = "test-results/varied.png",
            FullPath = variedPath
        });

        Assert.Equal(ScreenshotVisualReviewStatus.Fail, dark.Status);
        Assert.Equal(ScreenshotVisualReviewStatus.Pass, varied.Status);
        Assert.True(varied.BrightnessVariance > dark.BrightnessVariance);
    }

    [Fact]
    public void ScreenshotVisualReviewService_ReturnsHeuristicEvidence()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "test-results"));
        SaveTestPng(Path.Combine(root, "test-results", "valid.png"), 160, 120, (x, y) => ((byte)((x * 17 + y) % 255), (byte)((y * 13 + x) % 255), (byte)((x * y) % 255)));

        var evidence = new ScreenshotVisualReviewService().BuildEvidence(
            [new VerificationArtifact { Kind = "screenshot", Path = "test-results/valid.png" }],
            root);

        Assert.Contains(evidence, item => item.Contains("Screenshot visual review Pass", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(evidence, item => item.Contains("brightness=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VerificationArtifactEvidenceBuilder_QueuesValidScreenshotVisualReview()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "test-results"));
        SaveTestPng(Path.Combine(root, "test-results", "valid.png"), 160, 120, (x, y) => ((byte)((x * 17 + y) % 255), (byte)((y * 13 + x) % 255), (byte)((x * y) % 255)));

        var evidence = new VerificationArtifactEvidenceBuilder().BuildEvidence(
            [new VerificationArtifact { Kind = "screenshot", Path = "test-results/valid.png" }],
            root);

        Assert.Contains(evidence, item => item.Contains("Screenshot quality Valid", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(evidence, item => item.Contains("Screenshot visual review Pass", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScreenshotLlmVisionReviewer_SendsScreenshotAndParsesJson()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "test-results"));
        var screenshotPath = Path.Combine(root, "test-results", "valid.png");
        SaveTestPng(screenshotPath, 160, 120, (x, y) => ((byte)((x * 17 + y) % 255), (byte)((y * 13 + x) % 255), (byte)((x * y) % 255)));
        var provider = new CapturingLlmProvider("""{"status":"fail","summary":"Primary button overlaps the header.","findings":["Button text is clipped","Header overlaps content"]}""");

        var result = await new ScreenshotLlmVisionReviewer(provider).ReviewAsync(
            new ScreenshotLlmVisionReviewRequest
            {
                Candidate = new ScreenshotVisualReviewCandidate
                {
                    RelativePath = "test-results/valid.png",
                    FullPath = screenshotPath,
                    Reason = "Review for broken layout."
                },
                HeuristicResult = new ScreenshotVisualReviewResult
                {
                    RelativePath = "test-results/valid.png",
                    Status = ScreenshotVisualReviewStatus.Pass,
                    Message = "Screenshot passed first-pass heuristic review.",
                    AverageBrightness = 0.45,
                    BrightnessVariance = 0.02
                },
                Evidence = ["Playwright failed: button should be visible."],
                VerificationOutput = "1 failed"
            });

        Assert.Equal(ScreenshotLlmVisionReviewStatus.Fail, result.Status);
        Assert.Equal("Primary button overlaps the header.", result.Summary);
        Assert.Contains("Button text is clipped", result.Findings);
        var message = Assert.Single(provider.LastContext!.Messages);
        Assert.Contains(message.Content, content => content.Type == ContentType.Image &&
                                                   content.MediaType == "image/png" &&
                                                   !string.IsNullOrWhiteSpace(content.Base64Data));
        Assert.Contains(message.Content, content => content.Type == ContentType.Text &&
                                                   content.Text!.Contains("Heuristic status: Pass", StringComparison.OrdinalIgnoreCase) &&
                                                   content.Text.Contains("Playwright failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScreenshotLlmVisionReviewer_FallsBackForNonJsonResponse()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "test-results"));
        var screenshotPath = Path.Combine(root, "test-results", "valid.png");
        SaveTestPng(screenshotPath, 160, 120, (x, y) => ((byte)((x + y) % 255), (byte)((x * 2) % 255), (byte)((y * 3) % 255)));

        var result = await new ScreenshotLlmVisionReviewer(new CapturingLlmProvider("The page looks mostly okay.")).ReviewAsync(
            new ScreenshotLlmVisionReviewRequest
            {
                Candidate = new ScreenshotVisualReviewCandidate
                {
                    RelativePath = "test-results/valid.png",
                    FullPath = screenshotPath
                }
            });

        Assert.Equal(ScreenshotLlmVisionReviewStatus.Unknown, result.Status);
        Assert.Contains("mostly okay", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScreenshotLlmVisionEvidenceBuilder_SummarizesFindings()
    {
        var evidence = new ScreenshotLlmVisionEvidenceBuilder().BuildEvidence(
            new ScreenshotLlmVisionReviewResult
            {
                RelativePath = "test-results/failure.png",
                Status = ScreenshotLlmVisionReviewStatus.Warning,
                Summary = "The UI may be clipped.",
                Findings = ["Footer text is cut off", "Modal is too narrow"]
            });

        Assert.Contains("Screenshot LLM vision review Warning", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Footer text is cut off", evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DesktopScreenshotLlmVisionWorkflowService_RunsOnlyWhenEnabled()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "test-results"));
        var screenshotPath = Path.Combine(root, "test-results", "failure.png");
        SaveTestPng(screenshotPath, 160, 120, (x, y) => ((byte)((x * 11 + y) % 255), (byte)((y * 7 + x) % 255), (byte)((x + y) % 255)));
        var provider = new CapturingLlmProvider("""{"status":"warning","summary":"The submit button may be clipped.","findings":["Submit text is partially hidden"]}""");
        var service = new DesktopScreenshotLlmVisionWorkflowService(
            new CapturingLlmProviderFactory(provider),
            new ScreenshotVisualReviewService(),
            new ScreenshotVisualHeuristicEvaluator(),
            new ScreenshotLlmVisionEvidenceBuilder());
        var result = new VerificationRunResult
        {
            ExitCode = 1,
            StandardOutput = "1 failed",
            Artifacts = [new VerificationArtifact { Kind = "screenshot", Path = "test-results/failure.png" }]
        };

        Assert.Empty(await service.BuildEvidenceAsync(result, root, new ProviderConfiguration(), CancellationToken.None));

        var evidence = await service.BuildEvidenceAsync(
            result,
            root,
            new ProviderConfiguration
            {
                Provider = "openai",
                Model = "vision-model",
                DesktopEnableScreenshotLlmVisionReview = true
            },
            CancellationToken.None);

        Assert.Contains(evidence, item => item.Contains("Screenshot LLM vision review Warning", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(evidence, item => item.Contains("Submit text is partially hidden", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(provider.LastContext);
    }

    [Fact]
    public void DesktopPromptBuilder_PrioritizesScreenshotVisionEvidenceForAutoFix()
    {
        var prompt = DesktopPromptBuilder.BuildVerificationFixPrompt(
            new AgentVerificationPlan
            {
                Title = "Playwright",
                Command = "npm run test:e2e"
            },
            new VerificationRunResult
            {
                ExitCode = 1,
                StandardOutput = "1 failed"
            },
            new VerificationFailureAnalysis
            {
                Kind = VerificationFailureKind.TestFailure,
                Title = "Tests failed",
                Summary = "Playwright failed.",
                SuggestedNextStep = "Inspect the failing UI.",
                Evidence =
                [
                    "Screenshot LLM vision review Fail: test-results/login.png. Login button overlaps the modal title. Findings: Button text is clipped",
                    "Assert.True expected button to be visible"
                ]
            });

        Assert.Contains("Visual UI evidence from screenshot review:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Login button overlaps the modal title", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inspect the relevant UI component, style, layout, route, and test assertion", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Evidence:", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerificationResultCard_FailedHighlightsVisualEvidence()
    {
        var card = VerificationResultCard.Failed(
            new AgentVerificationPlan
            {
                Title = "Playwright",
                Command = "npm run test:e2e"
            },
            new VerificationRunResult
            {
                ExitCode = 1,
                StandardOutput = "1 failed"
            },
            new VerificationFailureAnalysis
            {
                Kind = VerificationFailureKind.TestFailure,
                Title = "Tests failed",
                Summary = "The browser test failed.",
                Evidence =
                [
                    "Screenshot LLM vision review Fail: test-results/login.png. Login button overlaps the modal title.",
                    "Assert.True expected button to be visible"
                ]
            },
            "Exit code: 1");

        Assert.True(card.HasVisualEvidence);
        Assert.Contains("Screenshot LLM vision review Fail", card.VisualEvidenceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Assert.True", card.VisualEvidenceSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerificationFailureClassifier_IncludesArtifactEvidence()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "frontend", "test-results", "login"));
        File.WriteAllBytes(Path.Combine(root, "frontend", "test-results", "login", "failure.png"), [1, 2, 3]);

        var analysis = new VerificationFailureClassifier().Analyze(
            new AgentVerificationPlan
            {
                Title = "E2E",
                Command = "cmd /c cd frontend && npm run test:e2e",
                Reason = "Run browser checks."
            },
            new VerificationRunResult
            {
                ExitCode = 1,
                StandardOutput = "1 failed",
                Artifacts =
                [
                    new VerificationArtifact
                    {
                        Kind = "screenshot",
                        Path = "frontend/test-results/login/failure.png",
                        Description = "Playwright screenshot evidence."
                    }
                ]
            },
            root);

        Assert.Equal(VerificationFailureKind.TestFailure, analysis.Kind);
        Assert.Contains(analysis.Evidence, evidence => evidence.Contains("frontend/test-results/login/failure.png", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Evidence, evidence => evidence.Contains("Screenshot quality TooSmall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AutoFixLoopGuard_StopsAfterThreeIdenticalFailures()
    {
        var guard = new AutoFixLoopGuard();

        var first = guard.RecordFailure(AutoFixLoopGuardState.Empty, "Tests failed|button hidden");
        var second = guard.RecordFailure(first.State, "Tests failed|button hidden");
        var third = guard.RecordFailure(second.State, "Tests failed|button hidden");

        Assert.False(first.ShouldStop);
        Assert.False(second.ShouldStop);
        Assert.True(third.ShouldStop);
        Assert.Equal(3, third.State.RepeatedCount);
        Assert.Contains("repeated 3", third.Message);
    }

    [Fact]
    public void AutoFixLoopGuard_ResetsCountForDifferentFailure()
    {
        var guard = new AutoFixLoopGuard();

        var first = guard.RecordFailure(AutoFixLoopGuardState.Empty, "Tests failed|button hidden");
        var second = guard.RecordFailure(first.State, "Compilation failed|CS1002");

        Assert.False(second.ShouldStop);
        Assert.Equal(1, second.State.RepeatedCount);
        Assert.Equal("Compilation failed|CS1002", second.State.FailureSignature);
    }

    [Fact]
    public async Task FileMutationSnapshotService_SavesSnapshotUnderWorkspace()
    {
        var root = CreateTempDirectory();
        var service = new FileMutationSnapshotService();

        var path = await service.SaveAsync(
            new FileMutationSnapshot
            {
                WorkspaceRoot = root,
                Path = Path.Combine(root, "src", "app.cs"),
                RelativePath = "src/app.cs",
                ExistedBefore = true,
                ExistsAfter = true,
                Before = "old",
                After = "new"
            },
            CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.StartsWith(Path.Combine(root, ".agentq", "snapshots"), path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Before\": \"old\"", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task DesktopFileChangeReviewService_RevertDeletesNewFile()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "new-file.txt");
        await File.WriteAllTextAsync(file, "created");
        var viewModel = new MainViewModel { WorkspaceRoot = root };
        var change = new FileChangeRecord
        {
            Path = file,
            RelativePath = "new-file.txt",
            Before = string.Empty,
            After = "created",
            ExistedBefore = false
        };

        await new DesktopFileChangeReviewService().RevertAsync(viewModel, change, CancellationToken.None);

        Assert.False(File.Exists(file));
        Assert.Equal(FileChangeReviewStatus.Reverted, change.ReviewStatus);
    }

    [Fact]
    public async Task DesktopTelemetryService_AppendsJsonlEvents()
    {
        var root = CreateTempDirectory();
        var service = new DesktopTelemetryService();

        await service.RecordAsync(
            new DesktopTelemetryEvent
            {
                EventType = "tool_completed",
                WorkspaceRoot = root,
                Provider = "openai",
                Model = "gpt-test",
                ToolName = "read_file",
                Succeeded = true,
                InputTokens = 10,
                OutputTokens = 5
            },
            CancellationToken.None);

        var path = DesktopTelemetryService.GetTelemetryPath(root);
        Assert.True(File.Exists(path));
        var line = Assert.Single(await File.ReadAllLinesAsync(path));
        Assert.Contains("\"eventType\":\"tool_completed\"", line);
        Assert.Contains("\"toolName\":\"read_file\"", line);
    }

    [Fact]
    public async Task VisualEvidenceService_DescribesImageAndVideoAttachments()
    {
        var root = CreateTempDirectory();
        var imagePath = Path.Combine(root, "screen.png");
        var videoPath = Path.Combine(root, "clip.mp4");
        await File.WriteAllBytesAsync(
            imagePath,
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAIAAADZrBkAAAAAD0lEQVR4nGNgYGD4z8DAwAAJAgMAv7lG8QAAAABJRU5ErkJggg=="));
        await File.WriteAllBytesAsync(videoPath, [0, 0, 0, 24, 102, 116, 121, 112]);

        var entries = VisualEvidenceService.InspectAttachments(
            [
                new DesktopAttachment
                {
                    Path = imagePath,
                    FileName = "screen.png",
                    MediaType = "image/png"
                },
                new DesktopAttachment
                {
                    Path = videoPath,
                    FileName = "clip.mp4",
                    MediaType = "video/mp4"
                }
            ]);

        Assert.Contains(entries, entry => entry.FileName == "screen.png" &&
                                          entry.Kind == "image" &&
                                          entry.Width == 2 &&
                                          entry.Height == 3);
        Assert.Contains(entries, entry => entry.FileName == "clip.mp4" &&
                                          entry.Kind == "video" &&
                                          entry.Width == 0 &&
                                          entry.Height == 0);

        var notes = VisualEvidenceService.BuildPromptNotes(
            [
                new DesktopAttachment
                {
                    Path = imagePath,
                    FileName = "screen.png",
                    MediaType = "image/png"
                }
            ]);

        Assert.Contains(notes, note => note.Contains("Visual evidence attached", StringComparison.OrdinalIgnoreCase) &&
                                       note.Contains("2x3", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProjectAgentConfigService_RoundTripsMcpServers()
    {
        var root = CreateTempDirectory();
        var service = new ProjectAgentConfigService();
        var config = new ProjectAgentConfig
        {
            WorkMode = AgentWorkMode.Coding.ToString(),
            McpServers =
            [
                new McpServerConfig
                {
                    Name = "blender",
                    Command = "uvx",
                    Args = ["blender-mcp"],
                    Tags = ["blender", "3d"]
                }
            ]
        };

        await service.SaveAsync(root, config, CancellationToken.None);
        var loaded = await service.LoadAsync(root, CancellationToken.None);

        Assert.NotNull(loaded);
        var server = Assert.Single(loaded.McpServers);
        Assert.Equal("blender", server.Name);
        Assert.Equal("uvx", server.Command);
        Assert.Equal("blender-mcp", Assert.Single(server.Args));
    }

    [Fact]
    public void McpServerRegistry_BuildsContextForEnabledServers()
    {
        var config = new ProjectAgentConfig
        {
            McpServers =
            [
                new McpServerConfig
                {
                    Name = "unity",
                    Command = "node",
                    Args = ["unity-mcp.js"],
                    Tags = ["trusted"]
                },
                new McpServerConfig
                {
                    Name = "disabled",
                    Command = "node",
                    Enabled = false
                }
            ]
        };

        var context = McpServerRegistry.BuildContext(config);

        Assert.Contains("Configured MCP servers", context);
        Assert.Contains("unity", context);
        Assert.DoesNotContain("disabled", context);
        Assert.Empty(McpServerRegistry.Validate(config));
    }

    [Fact]
    public void McpServerRegistry_DisablesUntrustedServers()
    {
        var config = new ProjectAgentConfig
        {
            McpServers =
            [
                new McpServerConfig
                {
                    Name = "untrusted",
                    Command = "node",
                    Args = ["server.js"]
                }
            ]
        };

        Assert.Empty(McpServerRegistry.EnabledServers(config));
        Assert.Contains(McpServerRegistry.Validate(config), warning => warning.Contains("trusted tag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void McpServerRegistry_BlocksWorkspaceLocalExecutables()
    {
        var root = CreateTempDirectory();
        var command = Path.Combine(root, "tools", "server.exe");
        var config = new ProjectAgentConfig
        {
            McpServers =
            [
                new McpServerConfig
                {
                    Name = "local",
                    Command = command,
                    Tags = ["trusted"]
                }
            ]
        };

        Assert.Empty(McpServerRegistry.EnabledServers(config, root));
        Assert.Contains(McpServerRegistry.Validate(config, root), warning => warning.Contains("workspace-local executable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void McpToolName_BuildsSafeAgentQToolName()
    {
        var name = McpToolName.Build("Unity Server", "scene/read-object");

        Assert.Equal("mcp_unity_server_scene_read_object", name);
    }

    [Fact]
    public async Task McpBridgeTool_CallsClientWithOriginalToolName()
    {
        var server = new McpServerConfig
        {
            Name = "unity",
            Command = "node"
        };
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""");
        var tool = new McpToolInfo
        {
            Name = "scene/read-object",
            Description = "Read a scene object.",
            InputSchema = schema.RootElement.Clone()
        };
        var client = new FakeMcpClient(JsonSerializer.SerializeToElement(new
        {
            content = new[] { new { type = "text", text = "ok" } }
        }));
        var bridge = new McpBridgeTool("mcp_unity_scene_read_object", server, tool, client);

        var result = await bridge.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = "Assets/Scene.unity" },
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("scene/read-object", client.LastToolName);
        Assert.Equal("Assets/Scene.unity", client.LastArguments.GetProperty("path").GetString());
        Assert.Contains("ok", result.Content);
        Assert.True(bridge.RequiresPermission);
    }

    [Fact]
    public async Task StdioMcpClient_ReusesInitializedSessionForToolCalls()
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershell))
        {
            return;
        }

        var root = CreateTempDirectory();
        var scriptPath = Path.Combine(root, "mcp-server.ps1");
        await File.WriteAllTextAsync(scriptPath, """
$pidValue = $PID
$callCount = 0
while (($line = [Console]::In.ReadLine()) -ne $null) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $request = $line | ConvertFrom-Json
    if ($null -eq $request.id) {
        continue
    }

    if ($request.method -eq "initialize") {
        $response = @{
            jsonrpc = "2.0"
            id = $request.id
            result = @{
                protocolVersion = "2024-11-05"
                capabilities = @{}
                serverInfo = @{
                    name = "test-mcp"
                    version = "1.0"
                }
            }
        }
    }
    elseif ($request.method -eq "tools/list") {
        $response = @{
            jsonrpc = "2.0"
            id = $request.id
            result = @{
                tools = @(
                    @{
                        name = "echo"
                        description = "Echo with session state."
                        inputSchema = @{
                            type = "object"
                            additionalProperties = $true
                        }
                    }
                )
            }
        }
    }
    elseif ($request.method -eq "tools/call") {
        $callCount += 1
        $response = @{
            jsonrpc = "2.0"
            id = $request.id
            result = @{
                content = @(
                    @{
                        type = "text"
                        text = "pid=$pidValue;count=$callCount;tool=$($request.params.name)"
                    }
                )
            }
        }
    }
    else {
        $response = @{
            jsonrpc = "2.0"
            id = $request.id
            error = @{
                code = -32601
                message = "unknown method"
            }
        }
    }

    [Console]::Out.WriteLine(($response | ConvertTo-Json -Depth 20 -Compress))
    [Console]::Out.Flush()
}
""");

        var server = new McpServerConfig
        {
            Name = "stateful",
            Command = powershell,
            Args = ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
            WorkingDirectory = root
        };

        using var client = new StdioMcpClient();

        var tools = await client.ListToolsAsync(server, CancellationToken.None);
        var first = await client.CallToolAsync(server, "echo", JsonSerializer.SerializeToElement(new { value = 1 }), CancellationToken.None);
        var second = await client.CallToolAsync(server, "echo", JsonSerializer.SerializeToElement(new { value = 2 }), CancellationToken.None);

        Assert.Equal("echo", Assert.Single(tools).Name);
        Assert.Contains("count=1", ExtractMcpText(first), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("count=2", ExtractMcpText(second), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopEvidenceFormatter_ExplainsMcpToolCalls()
    {
        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "mcp_unity_scene_read_object",
            new Dictionary<string, object?> { ["path"] = "Assets/Scene.unity" },
            "C:\\repo");

        Assert.Contains("MCP tool called", evidence);
        Assert.Contains("permission", evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopProjectConfigBuilder_PreservesExistingMcpServers()
    {
        var existing =
            new McpServerConfig
            {
                Name = "unreal",
                Command = "python",
                Args = ["unreal_mcp.py"]
            };

        var config = DesktopProjectConfigBuilder.Build(
            AgentWorkMode.Coding,
            ["cmd /c test.cmd"],
            ["hint"],
            [existing]);

        Assert.Equal("unreal", Assert.Single(config.McpServers).Name);
        Assert.Contains("MCP servers", DesktopProjectConfigBuilder.BuildDisplay(config));
    }

    [Fact]
    public async Task ToolReplayService_SavesAndLoadsLatestSession()
    {
        var root = CreateTempDirectory();
        var service = new ToolReplayService();

        var path = await service.SaveAsync(
            new ToolReplaySession
            {
                WorkspaceRoot = root,
                Provider = "openai",
                Model = "gpt-test",
                PromptPreview = "change file",
                Entries =
                [
                    new ToolReplayEntry
                    {
                        ToolName = "read_file",
                        ToolUseId = "tool-1",
                        InputJson = "{\"path\":\"README.md\"}",
                        ResultPreview = "{\"content\":\"hello\"}",
                        IsError = false,
                        DurationMs = 12
                    }
                ]
            },
            CancellationToken.None);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        var loaded = await service.LoadLatestAsync(root, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("openai", loaded.Provider);
        Assert.Equal("read_file", Assert.Single(loaded.Entries).ToolName);
    }

    [Fact]
    public async Task EvalReplayDashboardService_SummarizesReplayTelemetryAndVerification()
    {
        var root = CreateTempDirectory();
        var replayService = new ToolReplayService();
        await replayService.SaveAsync(
            new ToolReplaySession
            {
                WorkspaceRoot = root,
                Provider = "openai",
                Model = "gpt-test",
                PromptPreview = "run tools",
                Entries =
                [
                    new ToolReplayEntry
                    {
                        ToolName = "shell_execute",
                        ResultPreview = "error CS1002 failed",
                        IsError = true,
                        DurationMs = 25
                    },
                    new ToolReplayEntry
                    {
                        ToolName = "read_file",
                        ResultPreview = "ok",
                        IsError = false,
                        DurationMs = 5
                    },
                    new ToolReplayEntry
                    {
                        ToolName = "grep_search",
                        ResultPreview = "no matches",
                        IsError = true,
                        DurationMs = 7
                    },
                    new ToolReplayEntry
                    {
                        ToolName = "symbol_search",
                        ResultPreview = "found Parser",
                        IsError = false,
                        DurationMs = 3
                    }
                ]
            },
            CancellationToken.None);
        var telemetry = new DesktopTelemetryService();
        await telemetry.RecordAsync(
            new DesktopTelemetryEvent
            {
                EventType = "tool_failed",
                WorkspaceRoot = root,
                ToolName = "shell_execute",
                Succeeded = false,
                IsError = true,
                Detail = "error CS1002 failed",
                InputTokens = 10,
                OutputTokens = 3
            },
            CancellationToken.None);

        var report = await new EvalReplayDashboardService(replayService).BuildAsync(
            root,
            [
                new VerificationResultCard
                {
                    Status = "FAILED",
                    Title = "Compile error",
                    Command = "dotnet build",
                    Summary = "Build failed",
                    Detail = "error CS1002 failed",
                    OutputPreview = "error CS1002 failed"
                }
            ],
            CancellationToken.None);

        Assert.Contains("replay tools", report.Summary);
        Assert.Contains(report.Metrics, metric => metric.Contains("Replay: 4 tools", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("Telemetry: 1 events", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("Latency: tools 40 ms", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("Slowest tool: shell_execute 25 ms", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("LLM usage: 10 input tokens / 3 output tokens", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("Tool routing: keyword-search 1 call(s), 1 failed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("Tool routing: symbol-search 1 call(s), 0 failed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("Verification: 0 passed, 1 failed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Findings, finding => finding.Contains("Tool failure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Findings, finding => finding.Contains("Recurring failure", StringComparison.OrdinalIgnoreCase) &&
                                                    finding.Contains("shell_execute", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.ReplayEntries, entry => entry.Contains("FAILED shell_execute", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.FailureFingerprints, fingerprint => fingerprint.StartsWith("failure-", StringComparison.OrdinalIgnoreCase) &&
                                                                  fingerprint.Contains("x2", StringComparison.OrdinalIgnoreCase) &&
                                                                  fingerprint.Contains("telemetry:shell_execute", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvalReplayDashboardService_SummarizesToolRoutingTrendAcrossRecentReplays()
    {
        var root = CreateTempDirectory();
        var replayService = new ToolReplayService();
        await replayService.SaveAsync(
            new ToolReplaySession
            {
                WorkspaceRoot = root,
                Provider = "openai",
                Model = "gpt-test",
                Entries =
                [
                    new ToolReplayEntry
                    {
                        ToolName = "grep_search",
                        ResultPreview = "no matches",
                        IsError = true,
                        DurationMs = 7
                    },
                    new ToolReplayEntry
                    {
                        ToolName = "read_file",
                        ResultPreview = "ok",
                        IsError = false,
                        DurationMs = 4
                    }
                ]
            },
            CancellationToken.None);
        await replayService.SaveAsync(
            new ToolReplaySession
            {
                WorkspaceRoot = root,
                Provider = "openai",
                Model = "gpt-test",
                Entries =
                [
                    new ToolReplayEntry
                    {
                        ToolName = "grep_search",
                        ResultPreview = "found",
                        IsError = false,
                        DurationMs = 5
                    },
                    new ToolReplayEntry
                    {
                        ToolName = "symbol_search",
                        ResultPreview = "found Parser",
                        IsError = false,
                        DurationMs = 3
                    }
                ]
            },
            CancellationToken.None);

        var report = await new EvalReplayDashboardService(replayService).BuildAsync(root, [], CancellationToken.None);

        Assert.Contains(report.Metrics, metric => metric.Contains("Tool routing trend: keyword-search 2 call(s), 1 failed across 2 run(s)", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("Tool routing trend: file-read 1 call(s), 0 failed across 1 run(s)", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("Tool routing trend: symbol-search 1 call(s), 0 failed across 1 run(s)", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvalReplayDashboardService_SurfacesUnsafeEditingSignals()
    {
        var root = CreateTempDirectory();
        var replayService = new ToolReplayService();
        await replayService.SaveAsync(
            new ToolReplaySession
            {
                WorkspaceRoot = root,
                Provider = "openai",
                Model = "gpt-test",
                PromptPreview = "refactor Unity controller",
                Entries =
                [
                    new ToolReplayEntry
                    {
                        ToolName = "edit_file",
                        ResultPreview = "Repeated edit failure detected for the same file and strategy. Stop retrying this exact edit.",
                        IsError = true,
                        DurationMs = 8
                    },
                    new ToolReplayEntry
                    {
                        ToolName = "write_file",
                        ResultPreview = "Refusing high-risk whole-file rewrite for Unity MonoBehaviour.",
                        IsError = true,
                        DurationMs = 4
                    }
                ]
            },
            CancellationToken.None);

        var report = await new EvalReplayDashboardService(replayService).BuildAsync(root, [], CancellationToken.None);

        Assert.Contains(report.Findings, finding => finding.Contains("Unsafe editing signal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Findings, finding => finding.Contains("Repeated edit failure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Findings, finding => finding.Contains("whole-file rewrite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DesktopSearchRetryService_BuildsCaseInsensitiveGrepRetryWhenEmpty()
    {
        var retries = DesktopSearchRetryService.BuildRetryInputs(
            "grep_search",
            new Dictionary<string, object?> { ["pattern"] = "ProjectMap" },
            """{"numMatches":0}""");

        Assert.Contains(retries, retry => retry.TryGetValue("pattern", out var pattern) &&
                                          string.Equals(pattern as string, "(?i)ProjectMap", StringComparison.Ordinal));
    }

    [Fact]
    public void DesktopSearchRetryService_BuildsRecursiveGlobRetryWhenEmpty()
    {
        var retries = DesktopSearchRetryService.BuildRetryInputs(
            "glob_search",
            new Dictionary<string, object?> { ["pattern"] = "*.cs" },
            """{"numFiles":0}""");

        Assert.Contains(retries, retry => retry.TryGetValue("pattern", out var pattern) &&
                                          string.Equals(pattern as string, "**/*.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void DesktopLearningSuggestionService_SuggestsWorkspaceMemoryCandidates()
    {
        var service = new DesktopLearningSuggestionService();
        var analysis = new WorkspaceAnalysis
        {
            ProjectType = ".NET",
            Framework = "net10.0-windows",
            ProjectMap = ["UI layer: csharp/AgentQ.Desktop", "Tests: csharp/AgentQ.Tests"],
            VerificationCommands = ["dotnet build", "dotnet test"],
            KeyFiles = ["README.md", "AgentQ.sln"]
        };

        var lessons = service.SuggestWorkspaceLessons(analysis);

        Assert.Contains(lessons, lesson => lesson.Tags.Contains("project-map"));
        Assert.Contains(lessons, lesson => lesson.Tags.Contains("verification"));
        Assert.Contains(lessons, lesson => lesson.Content.Contains("README.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DesktopWorkspaceAnalysisReportBuilder_IncludesReusableEvidence()
    {
        var report = DesktopWorkspaceAnalysisReportBuilder.Build(new WorkspaceAnalysis
        {
            WorkspaceRoot = "C:\\repo",
            ProjectType = "Node, Python",
            Framework = "React, FastAPI",
            GitBranch = "main",
            FileCount = 12,
            DirectoryCount = 4,
            VerificationCommands = ["npm run build", "python -m pytest"],
            ProjectMap = ["Frontend: frontend (evidence: frontend/package.json)"],
            KeyFiles = ["frontend/package.json", "backend/requirements.txt"],
            KeySymbols = ["route GET /health -> health"],
            Hints = ["Frontend/backend workspace detected."]
        });

        Assert.Contains("# Workspace Analysis Report", report);
        Assert.Contains("React, FastAPI", report);
        Assert.Contains("npm run build", report);
        Assert.Contains("evidence: frontend/package.json", report);
        Assert.Contains("backend/requirements.txt", report);
        Assert.Contains("route GET /health", report);
    }

    [Fact]
    public void DesktopWorkspaceAnalysisReportBuilder_CapsLargeSections()
    {
        var report = DesktopWorkspaceAnalysisReportBuilder.Build(new WorkspaceAnalysis
        {
            WorkspaceRoot = "C:\\repo",
            ProjectType = "Large",
            Framework = "Mixed",
            ProjectMap = Enumerable.Range(1, 45).Select(index => $"Layer {index}").ToList()
        });

        Assert.Contains("Layer 40", report);
        Assert.DoesNotContain("Layer 41", report);
        Assert.Contains("5 more omitted", report);
    }

    [Fact]
    public async Task EmbeddingIndexStore_SavesManifestUnderAgentQEmbeddings()
    {
        var root = CreateTempDirectory();
        var store = new EmbeddingIndexStore();
        var manifest = new EmbeddingIndexManifest
        {
            Provider = "openai",
            Model = "text-embedding-3-small",
            ChunkCount = 7,
            FileCount = 3
        };

        await store.SaveManifestAsync(root, manifest, CancellationToken.None);

        var paths = store.GetPaths(root);
        Assert.EndsWith(Path.Combine(".agentq", "embeddings", "index.json"), paths.IndexPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(paths.IndexPath));

        var loaded = await store.LoadManifestAsync(root, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("openai", loaded.Provider);
        Assert.Equal("text-embedding-3-small", loaded.Model);
        Assert.Equal(7, loaded.ChunkCount);
        Assert.Equal(3, loaded.FileCount);
    }

    [Fact]
    public async Task EmbeddingIndexBuilder_BuildsTextChunksAndSkipsIgnoredDirectories()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "AuthService.cs"),
            string.Join(Environment.NewLine, Enumerable.Range(1, 80).Select(i => $"public void Login{i}() {{ }}")));
        await File.WriteAllTextAsync(Path.Combine(root, "bin", "Generated.cs"), "should be ignored");

        var store = new EmbeddingIndexStore();
        var builder = new EmbeddingIndexBuilder(store);
        var result = await builder.BuildTextChunkIndexAsync(root, ct: CancellationToken.None);
        var loadedChunks = await store.LoadChunksAsync(root, CancellationToken.None);

        Assert.True(File.Exists(result.Paths.ChunksPath));
        Assert.True(result.Manifest.ChunkCount > 0);
        Assert.Equal(result.Manifest.ChunkCount, loadedChunks.Count);
        Assert.Contains(loadedChunks, chunk => chunk.RelativePath == "src/AuthService.cs");
        Assert.DoesNotContain(loadedChunks, chunk => chunk.RelativePath.Contains("bin", StringComparison.OrdinalIgnoreCase));
        Assert.All(loadedChunks, chunk => Assert.NotEmpty(chunk.FileHash));
    }

    [Fact]
    public async Task OpenAiEmbeddingClient_SendsEmbeddingRequestAndReturnsVectors()
    {
        using var factory = new StubHttpClientFactory(
            """
            {
              "data": [
                { "index": 1, "embedding": [0.3, 0.4] },
                { "index": 0, "embedding": [0.1, 0.2] }
              ]
            }
            """);
        using var client = factory.CreateClient("openai");
        client.BaseAddress = new Uri("https://api.openai.test/v1/");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-key");
        var embeddingClient = new OpenAiEmbeddingClient(client);

        var vectors = await embeddingClient.CreateEmbeddingsAsync(
            ["first chunk", "second chunk"],
            "text-embedding-3-small",
            CancellationToken.None);

        Assert.Equal("/v1/embeddings", factory.LastRequest?.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", factory.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("test-key", factory.LastRequest?.Headers.Authorization?.Parameter);
        Assert.Equal(2, vectors.Count);
        Assert.Equal([0.1f, 0.2f], vectors[0]);
        Assert.Equal([0.3f, 0.4f], vectors[1]);

        var body = factory.LastRequestBody;
        using var document = JsonDocument.Parse(body);
        Assert.Equal("text-embedding-3-small", document.RootElement.GetProperty("model").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("input").GetArrayLength());
    }

    [Fact]
    public void DesktopEmbeddingClientFactory_SupportsOpenAiAndCustomEmbeddings()
    {
        Assert.True(DesktopEmbeddingClientFactory.SupportsProvider("openai"));
        Assert.True(DesktopEmbeddingClientFactory.SupportsProvider("custom"));
        Assert.False(DesktopEmbeddingClientFactory.SupportsProvider("opencode-go"));
        Assert.Equal("text-embedding-3-small", DesktopEmbeddingClientFactory.ResolveEmbeddingModel("openai"));
        Assert.Equal(string.Empty, DesktopEmbeddingClientFactory.ResolveEmbeddingModel("opencode-go"));
    }

    [Fact]
    public void MainViewModel_ShowsBaseUrlOnlyForCustomProviders()
    {
        var viewModel = new MainViewModel();

        viewModel.Provider = "opencode-go";
        viewModel.EmbeddingProvider = "openai";
        Assert.False(viewModel.ShowBaseUrlSettings);
        Assert.True(viewModel.ShowEmbeddingSettings);
        Assert.False(viewModel.ShowEmbeddingBaseUrlSettings);

        viewModel.Provider = "custom";
        viewModel.EmbeddingProvider = "custom";
        Assert.True(viewModel.ShowBaseUrlSettings);
        Assert.True(viewModel.ShowEmbeddingSettings);
        Assert.True(viewModel.ShowEmbeddingBaseUrlSettings);

        viewModel.EmbeddingProvider = "none";
        Assert.False(viewModel.ShowEmbeddingSettings);
    }

    [Fact]
    public void DesktopEmbeddingClientFactory_UsesEmbeddingConfiguration()
    {
        var factory = new DesktopEmbeddingClientFactory();
        var client = factory.Create(new ProviderConfiguration
        {
            Provider = "opencode-go",
            ApiKey = "chat-key",
            EmbeddingProvider = "openai",
            EmbeddingBaseUrl = "https://api.openai.test/v1",
            EmbeddingApiKey = "embedding-key"
        });

        Assert.IsType<OpenAiEmbeddingClient>(client);
    }

    [Fact]
    public async Task EmbeddingIndexBuilder_FillsVectorsWithEmbeddingClient()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "AuthService.cs"), "public void Login() { }");

        var store = new EmbeddingIndexStore();
        var builder = new EmbeddingIndexBuilder(store);
        var result = await builder.BuildVectorIndexAsync(
            root,
            new FakeEmbeddingClient(),
            provider: "opencode-go",
            model: "text-embedding-3-small",
            maximumEmbeddedChunks: 10,
            ct: CancellationToken.None);

        var loadedChunks = await store.LoadChunksAsync(root, CancellationToken.None);
        Assert.Equal("opencode-go", result.Manifest.Provider);
        Assert.Contains(loadedChunks, chunk => chunk.Vector.Length == 2);
    }

    [Fact]
    public async Task DesktopSemanticSearchTool_ReturnsHighestSimilarityChunk()
    {
        var root = CreateTempDirectory();
        var store = new EmbeddingIndexStore();
        await store.SaveChunksAsync(
            root,
            [
                new EmbeddingIndexChunk
                {
                    Id = "auth",
                    RelativePath = "src/AuthService.cs",
                    Content = "public void Login() { }",
                    StartLine = 1,
                    EndLine = 1,
                    Vector = [0, 1]
                },
                new EmbeddingIndexChunk
                {
                    Id = "billing",
                    RelativePath = "src/BillingService.cs",
                    Content = "public void Charge() { }",
                    StartLine = 1,
                    EndLine = 1,
                    Vector = [1, 0]
                }
            ],
            CancellationToken.None);
        var tool = new DesktopSemanticSearchTool(store, new FakeEmbeddingClient(), root, "text-embedding-3-small");

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "login issue", ["limit"] = 1 },
            CancellationToken.None);

        Assert.False(result.IsError);
        using var document = JsonDocument.Parse(result.Content);
        Assert.Equal(1, document.RootElement.GetProperty("numResults").GetInt32());
        var first = document.RootElement.GetProperty("results")[0];
        Assert.Equal("src/AuthService.cs", first.GetProperty("RelativePath").GetString());
        Assert.True(first.GetProperty("Score").GetDouble() > 0.99);
    }

    [Fact]
    public async Task DesktopSymbolSearchTool_ReturnsMatchingSymbols()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "backend"));
        Directory.CreateDirectory(Path.Combine(root, "frontend"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "auth.py"),
            """
            class AuthService:
                def login_user(self):
                    return True
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "auth.ts"),
            """
            export function loginUser() {
                return true;
            }
            """);
        var tool = new DesktopSymbolSearchTool(root);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "login", ["limit"] = 5 },
            CancellationToken.None);

        Assert.False(result.IsError);
        using var document = JsonDocument.Parse(result.Content);
        Assert.True(document.RootElement.GetProperty("indexedSymbols").GetInt32() >= 3);
        Assert.True(document.RootElement.GetProperty("numResults").GetInt32() >= 2);
        var results = document.RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.Contains(results, item => item.GetProperty("Name").GetString() == "login_user");
        Assert.Contains(results, item => item.GetProperty("Name").GetString() == "loginUser");
    }

    [Fact]
    public async Task DesktopHybridSearchTool_RanksSymbolKeywordAndSemanticCandidates()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "AuthService.cs"),
            """
            public sealed class AuthService
            {
                public bool LoginUser(string email) => true;
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "BillingService.cs"),
            """
            public sealed class BillingService
            {
                public bool Charge() => true;
            }
            """);

        var store = new EmbeddingIndexStore();
        await store.SaveChunksAsync(
            root,
            [
                new EmbeddingIndexChunk
                {
                    Id = "auth",
                    RelativePath = "src/AuthService.cs",
                    Content = "login authentication flow",
                    StartLine = 1,
                    EndLine = 4,
                    Vector = [0, 1]
                },
                new EmbeddingIndexChunk
                {
                    Id = "billing",
                    RelativePath = "src/BillingService.cs",
                    Content = "billing charge payment",
                    StartLine = 1,
                    EndLine = 4,
                    Vector = [1, 0]
                }
            ],
            CancellationToken.None);
        var tool = new DesktopHybridSearchTool(root, store, new FakeEmbeddingClient(), "text-embedding-3-small");

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "LoginUser", ["limit"] = 3 },
            CancellationToken.None);

        Assert.False(result.IsError);
        using var document = JsonDocument.Parse(result.Content);
        Assert.True(document.RootElement.GetProperty("numResults").GetInt32() >= 1);
        var first = document.RootElement.GetProperty("results")[0];
        Assert.Equal("src/AuthService.cs", first.GetProperty("RelativePath").GetString());
        var sources = first.GetProperty("Sources").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("symbol", sources);
        Assert.Contains("keyword", sources);
    }

    [Fact]
    public async Task DesktopHybridSearchTool_AddsGraphNeighborsToRankedCandidates()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "LoginPage.tsx"),
            """
            import * as auth from "./auth";

            export function LoginPage() {
                return auth;
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "auth.ts"),
            """
            export function loginUser(email: string) {
                return email.length > 0;
            }
            """);

        var tool = new DesktopHybridSearchTool(root, new EmbeddingIndexStore(), null, string.Empty);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "loginUser", ["limit"] = 5, ["includeSemantic"] = false },
            CancellationToken.None);

        Assert.False(result.IsError);
        using var document = JsonDocument.Parse(result.Content);
        var results = document.RootElement.GetProperty("results").EnumerateArray().ToList();
        var loginPage = Assert.Single(results, item => item.GetProperty("RelativePath").GetString() == "src/LoginPage.tsx");
        var sources = loginPage.GetProperty("Sources").EnumerateArray().Select(item => item.GetString()).ToList();
        var reasons = loginPage.GetProperty("Reasons").EnumerateArray().Select(item => item.GetString()).ToList();

        Assert.Contains("graph", sources);
        Assert.Contains(reasons, reason => reason?.Contains("imports candidate src/auth.ts", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task DesktopHybridSearchTool_AddsMemorySignalsToExistingCandidates()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, ".agentq"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "auth.ts"),
            """
            export function loginUser(email: string) {
                return email.length > 0;
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, ".agentq", "memory.local.json"),
            """
            {
              "version": 1,
              "lessons": [
                {
                  "id": "auth-login",
                  "title": "loginUser auth location",
                  "content": "loginUser changes usually belong in src/auth.ts.",
                  "tags": ["auth", "loginUser"],
                  "enabled": true
                }
              ]
            }
            """);

        var tool = new DesktopHybridSearchTool(root, new EmbeddingIndexStore(), null, string.Empty);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "loginUser", ["limit"] = 3, ["includeSemantic"] = false },
            CancellationToken.None);

        Assert.False(result.IsError);
        using var document = JsonDocument.Parse(result.Content);
        var auth = Assert.Single(
            document.RootElement.GetProperty("results").EnumerateArray(),
            item => item.GetProperty("RelativePath").GetString() == "src/auth.ts");
        var sources = auth.GetProperty("Sources").EnumerateArray().Select(item => item.GetString()).ToList();

        Assert.Contains("memory", sources);
    }

    [Fact]
    public async Task ProjectMemoryService_LoadsWorkspaceLocalAndSharedMemory()
    {
        var root = CreateTempDirectory();
        var agentQDirectory = Path.Combine(root, ".agentq");
        Directory.CreateDirectory(agentQDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(agentQDirectory, "memory.shared.json"),
            """
            {
              "version": 1,
              "workspaceRules": [ "Run dotnet test before commits." ],
              "lessons": [
                {
                  "id": "desktop-exe-lock",
                  "title": "Close desktop before tests",
                  "content": "AgentQ.Desktop.exe can lock the build output during dotnet test.",
                  "tags": [ "desktop", "test" ],
                  "confidence": 0.95,
                  "source": "test failure"
                }
              ],
              "preferences": [
                { "key": "shell", "value": "cmd" }
              ],
              "checks": [
                { "name": "secret scan", "command": "rg sk-", "when": "before_push" }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(agentQDirectory, "memory.local.json"),
            """
            {
              "version": 1,
              "projectHints": [ "User prefers a single main branch." ],
              "lessons": [
                {
                  "id": "local-language",
                  "title": "Answer language",
                  "content": "Answer in Korean unless the user asks otherwise.",
                  "confidence": 0.9
                }
              ]
            }
            """);

        var service = new ProjectMemoryService();
        var memory = await service.LoadOrDiscoverAsync(root, CancellationToken.None);
        var context = service.BuildContext(memory);

        Assert.Contains("Run dotnet test before commits.", memory.WorkspaceRules);
        Assert.Contains(memory.ProjectHints, hint => hint.Contains("memory.shared.json", StringComparison.Ordinal));
        Assert.Contains(memory.ProjectHints, hint => hint.Contains("memory.local.json", StringComparison.Ordinal));
        Assert.Contains(memory.Lessons, lesson => lesson.Id == "desktop-exe-lock");
        Assert.Contains(memory.Lessons, lesson => lesson.Id == "local-language");
        Assert.Contains(memory.Preferences, preference => preference.Key == "shell" && preference.Value == "cmd");
        Assert.Contains(memory.Checks, check => check.Name == "secret scan");
        Assert.Contains("Learned lessons:", context);
        Assert.Contains("AgentQ.Desktop.exe can lock", context);
        Assert.Contains("User/project preferences:", context);
        Assert.Contains("Remembered checks:", context);
    }

    [Fact]
    public async Task ProjectMemoryService_LoadsAndQueriesContextBank()
    {
        var root = CreateTempDirectory();
        var agentQDirectory = Path.Combine(root, ".agentq");
        Directory.CreateDirectory(agentQDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(agentQDirectory, "memory.local.json"),
            """
            {
              "version": 1,
              "contextBank": {
                "stack": [
                  { "key": "frontend", "value": "React + Vite", "tags": [ "frontend" ] }
                ],
                "preferences": [
                  { "key": "language", "value": "Korean" }
                ],
                "forbiddenPatterns": [
                  { "key": "state", "value": "Do not add Redux to this project." }
                ],
                "keyCommands": [
                  { "key": "frontend-build", "value": "cmd /c cd frontend && npm run build" }
                ],
                "keyFiles": [
                  { "key": "frontend-package", "value": "frontend/package.json" }
                ],
                "keySymbols": [
                  { "key": "DashboardView", "value": "class DashboardView (frontend/src/App.tsx:1)" }
                ],
                "recurringErrors": [
                  { "key": "vite-build", "value": "Vite build can fail when generated files are stale.", "tags": [ "error-history", "vite" ] }
                ]
              }
            }
            """);

        var service = new ProjectMemoryService();
        var memory = await service.LoadOrDiscoverAsync(root, CancellationToken.None);
        var context = service.BuildContext(memory, "vite frontend build error");

        Assert.Contains(memory.ContextBank.Stack, fact => fact.Value == "React + Vite");
        Assert.Contains(memory.ContextBank.KeySymbols, fact => fact.Key == "DashboardView");
        Assert.Contains("Context bank:", context);
        Assert.Contains("frontend-build", context);
        Assert.Contains("vite-build", context);
        Assert.DoesNotContain("Do not add Redux", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectMemoryService_EnrichesContextBankFromWorkspaceAnalysis()
    {
        var root = CreateTempDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(root, "package.json"),
            """{"dependencies":{"react":"latest"},"devDependencies":{"vite":"latest"},"scripts":{"build":"vite build"}}""");

        var service = new ProjectMemoryService();
        var memory = await service.LoadOrDiscoverAsync(root, CancellationToken.None);
        var context = service.BuildContext(memory, "react vite build");

        Assert.Contains(memory.ContextBank.Stack, fact => fact.Key == "project-type" && fact.Value.Contains("Node", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(memory.ContextBank.Stack, fact => fact.Key == "framework" && fact.Value.Contains("Vite", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(memory.ContextBank.KeyCommands, fact => fact.Value.Contains("npm run build", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Context bank:", context);
    }

    [Fact]
    public async Task ProjectMemoryService_SkipsSensitiveMemoryEntries()
    {
        var root = CreateTempDirectory();
        var agentQDirectory = Path.Combine(root, ".agentq");
        Directory.CreateDirectory(agentQDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(agentQDirectory, "memory.local.json"),
            """
            {
              "version": 1,
              "projectHints": [ "api_key=sk-test-secret" ],
              "lessons": [
                {
                  "id": "unsafe",
                  "title": "Leaked token",
                  "content": "Use bearer token abc.",
                  "confidence": 1
                }
              ],
              "preferences": [
                { "key": "api", "value": "secret value" }
              ],
              "checks": [
                { "name": "unsafe", "command": "echo sk-test-secret", "when": "never" }
              ]
            }
            """);

        var service = new ProjectMemoryService();
        var memory = await service.LoadOrDiscoverAsync(root, CancellationToken.None);
        var context = service.BuildContext(memory);

        Assert.DoesNotContain(memory.ProjectHints, hint => hint.Contains("sk-test-secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(memory.Lessons, lesson => lesson.Id == "unsafe");
        Assert.DoesNotContain(memory.Preferences, preference => preference.Key == "api");
        Assert.DoesNotContain(memory.Checks, check => check.Name == "unsafe");
        Assert.DoesNotContain("sk-test-secret", context, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer token", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectMemoryService_SkipsDisabledExpiredAndDangerousMemoryEntries()
    {
        var root = CreateTempDirectory();
        var agentQDirectory = Path.Combine(root, ".agentq");
        Directory.CreateDirectory(agentQDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(agentQDirectory, "memory.local.json"),
            $$"""
            {
              "version": 1,
              "lessons": [
                {
                  "id": "disabled",
                  "title": "Disabled lesson",
                  "content": "This disabled memory should not be used.",
                  "enabled": false,
                  "confidence": 0.9
                },
                {
                  "id": "expired",
                  "title": "Expired lesson",
                  "content": "This expired memory should not be used.",
                  "expiresAt": "{{DateTime.Now.AddDays(-1):O}}",
                  "confidence": 0.9
                },
                {
                  "id": "active",
                  "title": "Active lesson",
                  "content": "This active memory should be used.",
                  "source": "test",
                  "confidence": 0.8
                }
              ],
              "preferences": [
                { "key": "disabled", "value": "skip me", "enabled": false },
                { "key": "language", "value": "Korean" }
              ],
              "checks": [
                { "name": "danger", "command": "git reset --hard", "when": "never" },
                { "name": "tests", "command": "dotnet test .\\csharp\\AgentQ.sln", "when": "before_push" }
              ]
            }
            """);

        var service = new ProjectMemoryService();
        var memory = await service.LoadOrDiscoverAsync(root, CancellationToken.None);
        var context = service.BuildContext(memory);

        Assert.DoesNotContain("disabled memory", context, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expired memory", context, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("skip me", context, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git reset --hard", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("This active memory should be used.", context);
        Assert.Contains("language: Korean", context);
        Assert.Contains("dotnet test", context);
    }

    [Fact]
    public async Task ProjectMemoryService_AddLocalLessonAsync_SavesApprovedLesson()
    {
        var root = CreateTempDirectory();
        var service = new ProjectMemoryService();
        await service.AddLocalLessonAsync(
            root,
            new ProjectMemoryLesson
            {
                Title = "Close desktop before tests",
                Content = "AgentQ.Desktop.exe can lock the build output during dotnet test.",
                Tags = ["desktop", "test"],
                Confidence = 0.9,
                Source = "approved candidate"
            },
            CancellationToken.None);

        var memory = await service.LoadOrDiscoverAsync(root, CancellationToken.None);

        Assert.Contains(memory.Lessons, lesson =>
            lesson.Title == "Close desktop before tests" &&
            lesson.Content.Contains("lock the build output", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(root, ".agentq", "memory.local.json")));
    }

    [Fact]
    public async Task ProjectMemoryService_AddLocalLessonAsync_MergesDuplicateLessons()
    {
        var root = CreateTempDirectory();
        var service = new ProjectMemoryService();

        await service.AddLocalLessonAsync(
            root,
            new ProjectMemoryLesson
            {
                Id = "first",
                Title = "Close desktop before tests",
                Content = "Close AgentQ.Desktop.exe before running dotnet test.",
                Tags = ["desktop"],
                Confidence = 0.5,
                Source = "first"
            },
            CancellationToken.None);
        await service.AddLocalLessonAsync(
            root,
            new ProjectMemoryLesson
            {
                Id = "second",
                Title = "Close desktop before tests",
                Content = "Close AgentQ.Desktop.exe before running dotnet test.",
                Tags = ["test"],
                Confidence = 0.9,
                Source = "second"
            },
            CancellationToken.None);

        var lessons = await service.LoadLocalLessonsAsync(root, CancellationToken.None);

        var lesson = Assert.Single(lessons);
        Assert.Equal("first", lesson.Id);
        Assert.Equal(0.9, lesson.Confidence);
        Assert.Contains("desktop", lesson.Tags);
        Assert.Contains("test", lesson.Tags);
    }

    [Fact]
    public void ProjectMemoryService_BuildContext_SkipsLowConfidenceAndStaleLessons()
    {
        var service = new ProjectMemoryService();
        var memory = new ProjectMemory
        {
            WorkspaceRoot = "C:\\repo",
            Lessons =
            [
                new ProjectMemoryLesson
                {
                    Id = "low",
                    Title = "Low confidence",
                    Content = "This low confidence memory should not be used.",
                    Confidence = 0.1,
                    CreatedAt = DateTime.Now
                },
                new ProjectMemoryLesson
                {
                    Id = "stale",
                    Title = "Stale lesson",
                    Content = "This stale memory should not be used.",
                    Confidence = 0.9,
                    CreatedAt = DateTime.Now.AddDays(-200)
                },
                new ProjectMemoryLesson
                {
                    Id = "fresh",
                    Title = "Fresh lesson",
                    Content = "This fresh memory should be used.",
                    Confidence = 0.9,
                    CreatedAt = DateTime.Now
                }
            ]
        };

        var context = service.BuildContext(memory);

        Assert.DoesNotContain("low confidence memory", context, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stale memory", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh memory", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectMemoryService_BuildContext_PrioritizesRelevantLessons()
    {
        var service = new ProjectMemoryService();
        var memory = new ProjectMemory
        {
            WorkspaceRoot = "C:\\repo",
            Lessons =
            [
                new ProjectMemoryLesson
                {
                    Id = "docker",
                    Title = "Docker release build",
                    Content = "Use docker buildx when packaging Linux containers.",
                    Tags = ["docker", "release"],
                    Confidence = 0.7,
                    CreatedAt = DateTime.Now.AddDays(-1),
                    Source = "test"
                },
                new ProjectMemoryLesson
                {
                    Id = "desktop",
                    Title = "Desktop test lock",
                    Content = "Close AgentQ.Desktop.exe before running dotnet test because it can lock build outputs.",
                    Tags = ["desktop", "test"],
                    Confidence = 0.7,
                    CreatedAt = DateTime.Now,
                    Source = "test"
                }
            ]
        };

        var context = service.BuildContext(memory, "desktop test fails because build output is locked");

        Assert.True(
            context.IndexOf("Desktop test lock", StringComparison.Ordinal) <
            context.IndexOf("Docker release build", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectMemoryService_BuildContext_SurfacesRelevantErrorHistory()
    {
        var service = new ProjectMemoryService();
        var memory = new ProjectMemory
        {
            WorkspaceRoot = "C:\\repo",
            Lessons =
            [
                new ProjectMemoryLesson
                {
                    Id = "embedding-404",
                    Title = "Failure pattern: Embedding index failed",
                    Content = "A previous failure happened with openai/text-embedding-3-small. Detail: OpenAI embedding request failed with status 404.",
                    Tags = ["failure", "error-history", "embedding", "provider"],
                    Confidence = 0.8,
                    CreatedAt = DateTime.Now
                },
                new ProjectMemoryLesson
                {
                    Id = "style",
                    Title = "Use compact UI",
                    Content = "Use compact desktop UI spacing.",
                    Tags = ["ui"],
                    Confidence = 0.8,
                    CreatedAt = DateTime.Now
                }
            ]
        };

        var context = service.BuildContext(memory, "embedding index failed with 404");

        Assert.Contains("Previously seen failures:", context);
        Assert.Contains("Embedding index failed", context);
        Assert.True(
            context.IndexOf("Previously seen failures:", StringComparison.Ordinal) <
            context.IndexOf("Learned lessons:", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectMemoryService_SelectRelevantLessons_UsesRecencyAsTieBreaker()
    {
        var service = new ProjectMemoryService();
        var oldLesson = new ProjectMemoryLesson
        {
            Id = "old",
            Title = "Same topic",
            Content = "Use dotnet test for verification.",
            Confidence = 0.7,
            CreatedAt = DateTime.Now.AddDays(-10)
        };
        var recentLesson = new ProjectMemoryLesson
        {
            Id = "recent",
            Title = "Same topic",
            Content = "Use dotnet test for verification.",
            Confidence = 0.7,
            LastUsedAt = DateTime.Now
        };

        var selected = service.SelectRelevantLessons([oldLesson, recentLesson], "dotnet test", maxCount: 2);

        Assert.Equal("recent", selected[0].Id);
    }

    [Fact]
    public async Task ProjectMemoryService_TouchRelevantLocalLessons_UpdatesOnlyMatchingLocalLessons()
    {
        var root = CreateTempDirectory();
        var agentQDirectory = Path.Combine(root, ".agentq");
        Directory.CreateDirectory(agentQDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(agentQDirectory, "memory.local.json"),
            """
            {
              "version": 1,
              "lessons": [
                {
                  "id": "desktop",
                  "title": "Desktop test lock",
                  "content": "Close AgentQ.Desktop.exe before running dotnet test.",
                  "tags": [ "desktop", "test" ],
                  "confidence": 0.8
                },
                {
                  "id": "docker",
                  "title": "Docker release build",
                  "content": "Use docker buildx for container releases.",
                  "tags": [ "docker" ],
                  "confidence": 0.8
                }
              ]
            }
            """);

        var service = new ProjectMemoryService();
        var touched = await service.TouchRelevantLocalLessonsAsync(
            root,
            "desktop test output is locked",
            CancellationToken.None);
        var memory = await service.LoadOrDiscoverAsync(root, CancellationToken.None);

        Assert.Single(touched);
        Assert.Equal("desktop", touched[0].Id);
        Assert.NotNull(memory.Lessons.Single(lesson => lesson.Id == "desktop").LastUsedAt);
        Assert.Null(memory.Lessons.Single(lesson => lesson.Id == "docker").LastUsedAt);
    }

    [Fact]
    public async Task ProjectMemoryService_TouchRelevantLocalLessons_DoesNotModifySharedMemory()
    {
        var root = CreateTempDirectory();
        var agentQDirectory = Path.Combine(root, ".agentq");
        Directory.CreateDirectory(agentQDirectory);
        var sharedPath = Path.Combine(agentQDirectory, "memory.shared.json");
        await File.WriteAllTextAsync(
            sharedPath,
            """
            {
              "version": 1,
              "lessons": [
                {
                  "id": "shared-desktop",
                  "title": "Shared desktop test",
                  "content": "Shared desktop test lesson.",
                  "tags": [ "desktop" ],
                  "confidence": 0.8
                }
              ]
            }
            """);
        var before = await File.ReadAllTextAsync(sharedPath);

        var service = new ProjectMemoryService();
        var touched = await service.TouchRelevantLocalLessonsAsync(
            root,
            "desktop test",
            CancellationToken.None);
        var after = await File.ReadAllTextAsync(sharedPath);

        Assert.Empty(touched);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ProjectMemoryService_DisablesAndDeletesLocalLessons()
    {
        var root = CreateTempDirectory();
        var agentQDirectory = Path.Combine(root, ".agentq");
        Directory.CreateDirectory(agentQDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(agentQDirectory, "memory.local.json"),
            """
            {
              "version": 1,
              "lessons": [
                {
                  "id": "keep",
                  "title": "Keep lesson",
                  "content": "Keep this local lesson.",
                  "confidence": 0.8
                },
                {
                  "id": "remove",
                  "title": "Remove lesson",
                  "content": "Remove this local lesson.",
                  "confidence": 0.8
                }
              ]
            }
            """);

        var service = new ProjectMemoryService();

        Assert.True(await service.DisableLocalLessonAsync(root, "keep", CancellationToken.None));
        Assert.True(await service.DeleteLocalLessonAsync(root, "remove", CancellationToken.None));

        var lessons = await service.LoadLocalLessonsAsync(root, CancellationToken.None);

        var lesson = Assert.Single(lessons);
        Assert.Equal("keep", lesson.Id);
        Assert.False(lesson.Enabled);
    }

    [Fact]
    public void DesktopLearningSuggestionService_SuggestsStepLimitLesson()
    {
        var service = new DesktopLearningSuggestionService();
        var viewModel = new MainViewModel();

        var lessons = service.SuggestLessons(
            "continue the task",
            "Stopped after reaching the maximum tool steps (50).",
            viewModel);

        Assert.Contains(lessons, lesson => lesson.Title.Contains("Continue", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DesktopLearningSuggestionService_ClassifiesProviderFailureMemory()
    {
        var service = new DesktopLearningSuggestionService();
        var viewModel = new MainViewModel
        {
            Provider = "opencode-go",
            Model = "kimi-k2.6"
        };
        viewModel.AddRunStep(
            AgentRunState.Failed,
            "Run failed",
            "OpenAI-compatible request failed with status 400 (Bad Request).");

        var lessons = service.SuggestLessons("hello", "failed", viewModel);

        var lesson = Assert.Single(lessons, item => item.Tags.Contains("error-history"));
        Assert.Contains("opencode-go/kimi-k2.6", lesson.Content);
        Assert.Contains("provider", lesson.Tags);
    }

    [Fact]
    public void DesktopLearningSuggestionService_ClassifiesEmbeddingFailureMemory()
    {
        var service = new DesktopLearningSuggestionService();

        var lesson = service.CreateFailureLesson(
            "Embedding index failed",
            "OpenAI embedding request failed with status 404 (Not Found).",
            "openai",
            "text-embedding-3-small",
            "embedding failure");

        Assert.Contains("embedding", lesson.Tags);
        Assert.Contains("provider", lesson.Tags);
        Assert.Contains("openai/text-embedding-3-small", lesson.Content);
    }

    [Fact]
    public void FailureFingerprintService_NormalizesPathsAndLineNumbers()
    {
        var first = FailureFingerprintService.Create(
            "Compilation failed",
            "C:\\repo\\src\\Auth.cs(12,5): error CS1002: ; expected");
        var second = FailureFingerprintService.Create(
            "Compilation failed",
            "D:\\other\\src\\Auth.cs(44,7): error CS1002: ; expected");

        Assert.Equal(first, second);
        Assert.StartsWith("failure-", first);
    }

    [Fact]
    public void DesktopLearningSuggestionService_AddsFailureFingerprint()
    {
        var lesson = new DesktopLearningSuggestionService().CreateFailureLesson(
            "Tests failed",
            "Xunit.Sdk.EqualException: Assert.Equal() Failure",
            "openai",
            "gpt-5.4",
            "verification failure");

        Assert.StartsWith("failure-", lesson.FailureFingerprint);
        Assert.Contains("error-history", lesson.Tags);
    }

    [Fact]
    public async Task ProjectMemoryService_MergesRecurringFailureLessonsByFingerprint()
    {
        var root = CreateTempDirectory();
        var service = new ProjectMemoryService();
        var first = new ProjectMemoryLesson
        {
            Id = "first-failure",
            Title = "Failure pattern: Compilation failed",
            Content = "First failure in C:\\repo\\src\\Auth.cs(12,5): error CS1002: ; expected",
            Tags = ["failure", "error-history", "compile"],
            FailureFingerprint = FailureFingerprintService.Create(
                "Compilation failed",
                "C:\\repo\\src\\Auth.cs(12,5): error CS1002: ; expected"),
            Confidence = 0.6
        };
        var second = new ProjectMemoryLesson
        {
            Id = "second-failure",
            Title = "Failure pattern: Compilation failed again",
            Content = "Second failure in D:\\work\\src\\Auth.cs(44,7): error CS1002: ; expected",
            Tags = ["failure", "error-history", "verification"],
            FailureFingerprint = FailureFingerprintService.Create(
                "Compilation failed",
                "D:\\work\\src\\Auth.cs(44,7): error CS1002: ; expected"),
            Confidence = 0.9
        };

        await service.AddLocalLessonAsync(root, first, CancellationToken.None);
        await service.AddLocalLessonAsync(root, second, CancellationToken.None);

        var lessons = await service.LoadLocalLessonsAsync(root, CancellationToken.None);
        var lesson = Assert.Single(lessons);

        Assert.Equal("first-failure", lesson.Id);
        Assert.Equal(0.9, lesson.Confidence);
        Assert.Contains("compile", lesson.Tags);
        Assert.Contains("verification", lesson.Tags);
        Assert.Equal(first.FailureFingerprint, lesson.FailureFingerprint);
    }

    [Fact]
    public void ProjectMemoryGcService_RemovesExpiredLowConfidenceStaleAndDuplicates()
    {
        var now = DateTime.Now;
        var lessons = new List<ProjectMemoryLesson>
        {
            new() { Id = "keep", Title = "Keep", Content = "Useful current lesson", Confidence = 0.9, CreatedAt = now },
            new() { Id = "expired", Title = "Expired", Content = "Old lesson", Confidence = 0.9, ExpiresAt = now.AddDays(-1) },
            new() { Id = "low", Title = "Low", Content = "Weak lesson", Confidence = 0.1, CreatedAt = now },
            new() { Id = "stale", Title = "Stale", Content = "Unused lesson", Confidence = 0.9, CreatedAt = now.AddDays(-220) },
            new() { Id = "dup-a", Title = "Duplicate", Content = "Same content", Confidence = 0.9, CreatedAt = now },
            new() { Id = "dup-b", Title = "Duplicate", Content = "Same content", Confidence = 0.8, CreatedAt = now }
        };

        var report = new ProjectMemoryGcService().Apply(lessons);

        Assert.Equal(6, report.BeforeCount);
        Assert.Equal(2, report.AfterCount);
        Assert.Equal(4, report.RemovedCount);
        Assert.Contains(report.RemovedLessons, item => item.Id == "expired" && item.Reason == "expired");
        Assert.Contains(report.RemovedLessons, item => item.Id == "low" && item.Reason == "low confidence");
        Assert.Contains(report.RemovedLessons, item => item.Id == "stale" && item.Reason == "stale unused");
        Assert.Contains(report.RemovedLessons, item => item.Id == "dup-b" && item.Reason == "duplicate");
        Assert.Contains(lessons, lesson => lesson.Id == "keep");
        Assert.Contains(lessons, lesson => lesson.Id == "dup-a");
    }

    [Fact]
    public async Task ProjectMemoryService_CompactLocalLessonsAsync_RemovesGcCandidates()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".agentq"));
        await File.WriteAllTextAsync(
            Path.Combine(root, ".agentq", "memory.local.json"),
            $$"""
            {
              "lessons": [
                {
                  "id": "keep",
                  "title": "Keep",
                  "content": "Useful lesson",
                  "confidence": 0.9,
                  "createdAt": "{{DateTime.Now:o}}"
                },
                {
                  "id": "expired",
                  "title": "Expired",
                  "content": "Expired lesson",
                  "confidence": 0.9,
                  "expiresAt": "{{DateTime.Now.AddDays(-1):o}}"
                }
              ]
            }
            """);

        var service = new ProjectMemoryService();
        var report = await service.CompactLocalLessonsAsync(root, null, CancellationToken.None);
        var lessons = await service.LoadLocalLessonsAsync(root, CancellationToken.None);

        Assert.Equal(1, report.RemovedCount);
        Assert.Single(lessons);
        Assert.Equal("keep", lessons[0].Id);
    }

    [Fact]
    public async Task DesktopProviderModelDiscoveryService_FetchesOpenAiCompatibleModels()
    {
        using var factory = new StubHttpClientFactory(
            """
            {
              "data": [
                { "id": "text-embedding-3-large" },
                { "id": "gpt-5.9" },
                { "id": "gpt-5.9-mini" }
              ]
            }
            """);
        var service = new DesktopProviderModelDiscoveryService(factory, CreateTempDirectory());

        var models = await service.GetModelsAsync(new ProviderConfiguration
        {
            Provider = "openai",
            BaseUrl = "https://api.openai.test/v1",
            ApiKey = "test-key"
        });

        Assert.Contains("gpt-5.9", models);
        Assert.Contains("gpt-5.9-mini", models);
        Assert.DoesNotContain("text-embedding-3-large", models);
        Assert.Equal("Bearer", factory.LastRequest?.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task DesktopProviderModelDiscoveryService_FetchesGoogleModels()
    {
        using var factory = new StubHttpClientFactory(
            """
            {
              "models": [
                { "name": "models/gemini-3.2-flash" },
                { "name": "models/embedding-001" }
              ]
            }
            """);
        var service = new DesktopProviderModelDiscoveryService(factory, CreateTempDirectory());

        var models = await service.GetModelsAsync(new ProviderConfiguration
        {
            Provider = "google",
            ApiKey = "test-key"
        });

        Assert.Contains("gemini-3.2-flash", models);
        Assert.DoesNotContain("embedding-001", models);
        Assert.Contains("generativelanguage.googleapis.com", factory.LastRequest?.RequestUri?.Host);
    }

    [Fact]
    public async Task DesktopProviderModelDiscoveryService_FallsBackToCatalogWhenDiscoveryFails()
    {
        using var factory = new StubHttpClientFactory("{}", HttpStatusCode.Unauthorized);
        var service = new DesktopProviderModelDiscoveryService(factory, CreateTempDirectory());

        var models = await service.GetModelsAsync(new ProviderConfiguration
        {
            Provider = "anthropic",
            BaseUrl = "https://api.anthropic.com",
            ApiKey = "bad-key"
        });

        Assert.Contains("claude-sonnet-4-6", models);
    }

    [Fact]
    public void DesktopProviderFailureClassifier_DescribesProviderAuthErrors()
    {
        var description = DesktopProviderFailureClassifier.Describe(
            new HttpRequestException("bad api key", null, HttpStatusCode.Unauthorized));

        Assert.Equal("Provider authentication failed", description.Title);
        Assert.Contains("API key", description.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bad api key", description.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopProviderFailureClassifier_DescribesProviderRateLimits()
    {
        var description = DesktopProviderFailureClassifier.Describe(
            new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests));

        Assert.Equal("Provider rate limit reached", description.Title);
        Assert.Contains("Wait briefly", description.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopProviderFailureClassifier_DescribesOutputLengthErrors()
    {
        var description = DesktopProviderFailureClassifier.Describe(
            new InvalidOperationException("maximum context length exceeded"));

        Assert.Equal("Provider output length exceeded", description.Title);
        Assert.Contains("smaller chunks", description.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainViewModel_ApplyProviderModels_SelectsFirstModelWhenCurrentIsUnavailable()
    {
        var viewModel = new MainViewModel
        {
            Model = "old-model"
        };

        viewModel.ApplyProviderModels(["new-model", "new-model-mini"], preserveCurrentModel: true);

        Assert.Equal("new-model", viewModel.Model);
        Assert.Equal(["new-model", "new-model-mini"], viewModel.AvailableModels);
    }

    [Fact]
    public void MainViewModel_ToConfiguration_PersistsDesktopUiLanguage()
    {
        var viewModel = new MainViewModel
        {
            UiLanguage = "\uD55C\uAD6D\uC5B4"
        };

        var config = viewModel.ToConfiguration();

        Assert.Equal("\uD55C\uAD6D\uC5B4", config.DesktopUiLanguage);
    }

    [Fact]
    public void MainViewModel_ApplyConfiguration_RestoresDesktopUiLanguage()
    {
        var viewModel = new MainViewModel();

        viewModel.ApplyConfiguration(new ProviderConfiguration
        {
            Provider = "opencode-go",
            Model = "kimi-k2.6",
            BaseUrl = ProviderConfiguration.OpenCodeGoDefaultBaseUrl,
            DesktopUiLanguage = "\uD55C\uAD6D\uC5B4"
        });

        Assert.Equal("\uD55C\uAD6D\uC5B4", viewModel.UiLanguage);
        Assert.Equal("\uC124\uC815", viewModel.SettingsHeaderText);
        Assert.Equal("\uD559\uC2B5 \uD6C4\uBCF4", viewModel.LearningCandidatesText);
        Assert.Equal(DesktopLocalizer.UiText(DesktopText.SettingsHeader, useKoreanUi: true), viewModel.SettingsHeaderText);
        Assert.Equal(DesktopLocalizer.UiText(DesktopText.LearningCandidates, useKoreanUi: true), viewModel.LearningCandidatesText);
        Assert.Equal("Learning candidates", new MainViewModel().LearningCandidatesText);
        Assert.Equal("File", new MainViewModel().MenuFileText);
    }

    [Fact]
    public void MainViewModel_KoreanUiSnapshot_CoversPrimaryDesktopBindings()
    {
        var viewModel = new MainViewModel
        {
            UiLanguage = "\uD55C\uAD6D\uC5B4"
        };

        var snapshot = new Dictionary<string, string>
        {
            [nameof(viewModel.MenuFileText)] = viewModel.MenuFileText,
            [nameof(viewModel.SettingsHeaderText)] = viewModel.SettingsHeaderText,
            [nameof(viewModel.ProjectHeaderText)] = viewModel.ProjectHeaderText,
            [nameof(viewModel.ProjectFolderText)] = viewModel.ProjectFolderText,
            [nameof(viewModel.ChatHeaderText)] = viewModel.ChatHeaderText,
            [nameof(viewModel.AttachFilesText)] = viewModel.AttachFilesText,
            [nameof(viewModel.CodeBlockText)] = viewModel.CodeBlockText,
            [nameof(viewModel.SendText)] = viewModel.SendText,
            [nameof(viewModel.StatusPanelText)] = viewModel.StatusPanelText,
            [nameof(viewModel.RunLogText)] = viewModel.RunLogText,
            [nameof(viewModel.ChangePreviewText)] = viewModel.ChangePreviewText,
            [nameof(viewModel.EvidenceTrailText)] = viewModel.EvidenceTrailText,
            [nameof(viewModel.EvalDashboardText)] = viewModel.EvalDashboardText,
            [nameof(viewModel.LearningCandidatesText)] = viewModel.LearningCandidatesText,
            [nameof(viewModel.SavedMemoryText)] = viewModel.SavedMemoryText,
            [nameof(viewModel.SessionSummaryText)] = viewModel.SessionSummaryText,
            [nameof(viewModel.GitStatusText)] = viewModel.GitStatusText,
            [nameof(viewModel.WorkspaceProjectType)] = viewModel.WorkspaceProjectType,
            [nameof(viewModel.EvalDashboardSummary)] = viewModel.EvalDashboardSummary
        };

        Assert.Equal("\uD30C\uC77C", snapshot[nameof(viewModel.MenuFileText)]);
        Assert.Equal("\uC124\uC815", snapshot[nameof(viewModel.SettingsHeaderText)]);
        Assert.Equal("\uD504\uB85C\uC81D\uD2B8", snapshot[nameof(viewModel.ProjectHeaderText)]);
        Assert.Equal("\uD504\uB85C\uC81D\uD2B8 \uD3F4\uB354", snapshot[nameof(viewModel.ProjectFolderText)]);
        Assert.Equal("\uC0C8 \uB300\uD654", snapshot[nameof(viewModel.ChatHeaderText)]);
        Assert.Equal("\uCCA8\uBD80", snapshot[nameof(viewModel.AttachFilesText)]);
        Assert.Equal("\uCF54\uB4DC \uBE14\uB85D", snapshot[nameof(viewModel.CodeBlockText)]);
        Assert.Equal("\uC804\uC1A1\nCtrl+Enter", snapshot[nameof(viewModel.SendText)]);
        Assert.Equal("\uC0C1\uD0DC \uD328\uB110", snapshot[nameof(viewModel.StatusPanelText)]);
        Assert.Equal("\uC791\uC5C5 \uB85C\uADF8", snapshot[nameof(viewModel.RunLogText)]);
        Assert.Equal("\uBCC0\uACBD \uBBF8\uB9AC\uBCF4\uAE30", snapshot[nameof(viewModel.ChangePreviewText)]);
        Assert.Equal("\uADFC\uAC70 \uD750\uB984", snapshot[nameof(viewModel.EvidenceTrailText)]);
        Assert.Equal("\uD3C9\uAC00", snapshot[nameof(viewModel.EvalDashboardText)]);
        Assert.Equal("\uD559\uC2B5 \uD6C4\uBCF4", snapshot[nameof(viewModel.LearningCandidatesText)]);
        Assert.Equal("\uC800\uC7A5\uB41C \uBA54\uBAA8\uB9AC", snapshot[nameof(viewModel.SavedMemoryText)]);
        Assert.Equal("\uC138\uC158 \uC694\uC57D", snapshot[nameof(viewModel.SessionSummaryText)]);
        Assert.Contains("\uC0C1\uD0DC", snapshot[nameof(viewModel.GitStatusText)]);
        Assert.Contains("\uBD84\uC11D", snapshot[nameof(viewModel.WorkspaceProjectType)]);
        Assert.Contains("\uC0C8\uB85C\uACE0\uCE68", snapshot[nameof(viewModel.EvalDashboardSummary)]);
    }

    [Fact]
    public void DesktopAgentRunWorkflowService_RemoveThinkingPlaceholder_RemovesPendingAssistantMessage()
    {
        var viewModel = new MainViewModel();
        viewModel.Messages.Add(new ChatMessageViewModel { Role = "User", Content = "hello" });
        viewModel.Messages.Add(new ChatMessageViewModel { Role = "AgentQ", Content = "\uC0DD\uAC01\uC911..." });

        DesktopAgentRunWorkflowService.RemoveThinkingPlaceholder(viewModel);

        Assert.Single(viewModel.Messages);
        Assert.Equal("User", viewModel.Messages[0].Role);
    }

    [Fact]
    public void DesktopAgentRunWorkflowService_RemoveThinkingPlaceholder_KeepsStartedAssistantMessage()
    {
        var viewModel = new MainViewModel();
        viewModel.Messages.Add(new ChatMessageViewModel { Role = "AgentQ", Content = "partial response" });

        DesktopAgentRunWorkflowService.RemoveThinkingPlaceholder(viewModel);

        Assert.Single(viewModel.Messages);
        Assert.Equal("partial response", viewModel.Messages[0].Content);
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

        Assert.Equal("backup/20260520-103045", branchName);
    }

    [Theory]
    [InlineData("## main...origin/main [behind 2]", "Pull --ff-only")]
    [InlineData("## feature...origin/feature [ahead 1]", "Local commits exist")]
    [InlineData("## feature...origin/feature [ahead 1, behind 2]", "Branch diverged")]
    [InlineData("## feature...origin/feature [gone]", "Upstream is gone")]
    [InlineData("## local-only", "No upstream")]
    [InlineData("## HEAD (no branch)", "Detached HEAD")]
    public void GitBranchRecoveryAnalyzer_BuildsRecoveryAdvice(string statusOutput, string expected)
    {
        var advice = GitBranchRecoveryAnalyzer.BuildRecoveryAdvice(statusOutput, []);

        Assert.Contains(expected, advice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitBranchRecoveryAnalyzer_PrioritizesLocalChangesInRecoveryAdvice()
    {
        var advice = GitBranchRecoveryAnalyzer.BuildRecoveryAdvice(
            "## main...origin/main [behind 2]",
            [
                new GitChangedFile
                {
                    Status = " M",
                    Path = "README.md"
                }
            ]);

        Assert.Contains("1 local change", advice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Commit or stash", advice, StringComparison.OrdinalIgnoreCase);
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
    public void DesktopPlanParser_ParsesNamedTodoStatuses()
    {
        var items = DesktopPlanParser.Parse(
            """
            - [completed] Inspect workspace.
            - [in_progress] Patch fallback flow.
            - [pending] Run tests.
            - [cancelled] Remove obsolete branch.
            """);

        Assert.Equal(4, items.Count);
        Assert.Equal(AgentPlanItemStatus.Done, items[0].Status);
        Assert.Equal(AgentPlanItemStatus.InProgress, items[1].Status);
        Assert.Equal(AgentPlanItemStatus.Pending, items[2].Status);
        Assert.Equal(AgentPlanItemStatus.Blocked, items[3].Status);
    }

    [Fact]
    public void DesktopPromptAssembly_IncludesTaskTrackingRulesForCodingTasks()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("Add oauth support and then database integration finally test it");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base", profile);

        Assert.Contains("Task tracking rules:", prompt, StringComparison.Ordinal);
        Assert.Contains("exactly one item in progress", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("waiting-for-answer state", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkerPlanApprovalSummaryBuilder_SummarizesFilesRiskAndVerification()
    {
        var plan = new WorkerPlan
        {
            Goal = "Add account login",
            Language = "csharp",
            Framework = ".NET",
            Steps =
            [
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.ModifyFile,
                    Path = "src/AuthService.cs",
                    Reason = "Update login/session behavior.",
                    ExpectedChange = "Validate credentials and issue sessions.",
                    RequiresApproval = true
                },
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.ModifyFile,
                    Path = "src/UserService.cs",
                    Reason = "Load users for auth."
                },
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.CreateFile,
                    Path = "src/LoginRequestDto.cs",
                    ExpectedChange = "Add request DTO."
                },
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.CreateFile,
                    Path = "tests/AuthServiceTests.cs",
                    ExpectedChange = "Cover login success and failure."
                },
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.CreateFile,
                    Path = "db/migrations/20260529_add_login_sessions.sql",
                    ExpectedChange = "Create login session table."
                }
            ],
            VerificationCommands = ["dotnet test", "npx playwright test"],
            Risks = ["Database migration changes persisted schema."]
        };

        var summary = new WorkerPlanApprovalSummaryBuilder().Build(plan);

        Assert.Equal(3, summary.CreateCount);
        Assert.Equal(2, summary.ModifyCount);
        Assert.Equal(0, summary.DeleteCount);
        Assert.Equal(WorkerPlanRiskLevel.High, summary.RiskLevel);
        Assert.True(summary.HasHighRiskChanges);
        Assert.True(summary.CanApproveLowRiskOnly);
        Assert.Contains(summary.ModifiedFiles, file => file == "src/AuthService.cs");
        Assert.Contains(summary.CreatedFiles, file => file.Contains("migrations", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.ExpectedChanges, change => change.Contains("Validate credentials", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.RiskReasons, reason => reason.Contains("high-risk", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.RiskReasons, reason => reason.Contains("Database migration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.VerificationCommands, command => command == "dotnet test");
    }

    [Fact]
    public void WorkerPlanApprovalSummaryBuilder_KeepsSmallTestOnlyPlanLowRisk()
    {
        var plan = new WorkerPlan
        {
            Goal = "Add parser coverage",
            Language = "csharp",
            Steps =
            [
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.CreateFile,
                    Path = "tests/ParserTests.cs",
                    ExpectedChange = "Add parser edge-case tests."
                }
            ],
            VerificationCommands = ["dotnet test --filter ParserTests"]
        };

        var summary = new WorkerPlanApprovalSummaryBuilder().Build(plan);

        Assert.Equal(WorkerPlanRiskLevel.Low, summary.RiskLevel);
        Assert.False(summary.HasHighRiskChanges);
        Assert.False(summary.CanApproveLowRiskOnly);
        Assert.Equal(["tests/ParserTests.cs"], summary.CreatedFiles);
        Assert.Equal(["dotnet test --filter ParserTests"], summary.VerificationCommands);
    }

    [Fact]
    public void WorkerPlanCandidateBuilder_ConvertsScaffoldRecommendationToWorkerPlan()
    {
        var recommendation = new WorkerScaffoldRecommendation
        {
            Name = "React application feature",
            Description = "Create component, hook, API module, route/view integration, and Vitest coverage.",
            Files =
            [
                "src/features/<feature>/<FeatureView>.tsx",
                "src/features/<feature>/use<Feature>.ts",
                "src/features/<feature>/api.ts",
                "src/features/<feature>/<feature>.test.tsx"
            ],
            VerificationCommands = ["npm test", "npm run build"]
        };

        var plan = new WorkerPlanCandidateBuilder().BuildCandidate(
            "Create a dashboard feature",
            "typescript",
            "React",
            recommendation);

        Assert.Equal("Create a dashboard feature", plan.Goal);
        Assert.Equal("typescript", plan.Language);
        Assert.Equal("React", plan.Framework);
        Assert.Equal(4, plan.Steps.Count(step => step.Kind == WorkerPlanStepKind.CreateFile));
        Assert.Equal(2, plan.Steps.Count(step => step.Kind == WorkerPlanStepKind.Verify));
        Assert.Contains(plan.Steps, step => step.Path == "src/features/<feature>/<FeatureView>.tsx" &&
                                           step.ExpectedChange.Contains("FeatureView", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.VerificationCommands, command => command == "npm run build");
        Assert.Empty(plan.Risks);
    }

    [Fact]
    public void WorkerPlanCandidateBuilder_MarksDatabaseScaffoldAsApprovalRequired()
    {
        var recommendation = new WorkerScaffoldRecommendation
        {
            Name = "SQL migration",
            Description = "Create migration and rollback-friendly schema changes.",
            Files = ["db/migrations/<timestamp>_<feature>.sql"],
            VerificationCommands = ["sqlfluff lint"]
        };

        var plan = new WorkerPlanCandidateBuilder().BuildCandidate(
            "Add billing table",
            "sql",
            "PostgreSQL",
            recommendation);
        var summary = new WorkerPlanApprovalSummaryBuilder().Build(plan);

        Assert.Single(plan.Steps, step => step.Kind == WorkerPlanStepKind.CreateFile);
        Assert.Contains(plan.Steps, step => step.RequiresApproval &&
                                           step.Path == "db/migrations/<timestamp>_<feature>.sql");
        Assert.Contains(plan.Risks, risk => risk.Contains("high-risk", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(WorkerPlanRiskLevel.High, summary.RiskLevel);
        Assert.Contains(summary.VerificationCommands, command => command == "sqlfluff lint");
    }

    [Fact]
    public void WorkerPlanValidator_BlocksPathsOutsideWorkspace()
    {
        var root = CreateTempDirectory();
        var outside = Path.Combine(Path.GetTempPath(), $"agentq-outside-{Guid.NewGuid():N}.txt");
        var plan = new WorkerPlan
        {
            Steps =
            [
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.CreateFile,
                    Path = outside,
                    ExpectedChange = "Create outside file."
                }
            ]
        };

        var result = new WorkerPlanValidator().Validate(plan, root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "path_outside_workspace" &&
                                               issue.Severity == WorkerPlanValidationSeverity.Blocker);
    }

    [Fact]
    public void WorkerPlanValidator_RequiresApprovalForDeleteAndApprovalSteps()
    {
        var root = CreateTempDirectory();
        var plan = new WorkerPlan
        {
            Steps =
            [
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.DeleteFile,
                    Path = "src/OldService.cs"
                },
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.ModifyFile,
                    Path = "src/AuthService.cs",
                    RequiresApproval = true
                }
            ],
            VerificationCommands = ["cmd /c test.cmd"]
        };

        var result = new WorkerPlanValidator().Validate(plan, root);

        Assert.True(result.IsValid);
        Assert.True(result.RequiresApproval);
        Assert.Contains(result.Issues, issue => issue.Code == "delete_requires_approval");
        Assert.Contains(result.Issues, issue => issue.Code == "step_requires_approval");
    }

    [Fact]
    public void WorkerPlanValidator_BlocksUnsafeVerificationCommands()
    {
        var root = CreateTempDirectory();
        var plan = new WorkerPlan
        {
            Steps =
            [
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.Verify,
                    ExpectedChange = "Remove-Item -Recurse ."
                }
            ],
            VerificationCommands = ["Remove-Item -Recurse ."]
        };

        var result = new WorkerPlanValidator().Validate(plan, root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "verification_step_not_allowed");
        Assert.Contains(result.Issues, issue => issue.Code == "verification_command_not_allowed");
    }

    [Fact]
    public void WorkerPlanPreviewBuilder_MarksLowRiskValidPlanReady()
    {
        var root = CreateTempDirectory();
        var plan = new WorkerPlan
        {
            Goal = "Add parser tests",
            Steps =
            [
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.CreateFile,
                    Path = "tests/ParserTests.cs",
                    ExpectedChange = "Add parser coverage."
                }
            ],
            VerificationCommands = ["cmd /c test.cmd"]
        };

        var preview = new WorkerPlanPreviewBuilder().Build(plan, root);

        Assert.Equal(WorkerPlanApprovalState.Ready, preview.ApprovalState);
        Assert.True(preview.Validation.IsValid);
        Assert.Equal(WorkerPlanRiskLevel.Low, preview.ApprovalSummary.RiskLevel);
        Assert.Contains("1 create, 0 modify, 0 delete", preview.DecisionSummary);
        Assert.Contains("Ready", preview.DecisionSummary);
    }

    [Fact]
    public void WorkerPlanPreviewBuilder_MarksHighRiskValidPlanNeedsApproval()
    {
        var root = CreateTempDirectory();
        var plan = new WorkerPlan
        {
            Summary = "Create auth migration",
            Steps =
            [
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.CreateFile,
                    Path = "db/migrations/001_auth.sql",
                    ExpectedChange = "Add auth session table.",
                    RequiresApproval = true
                }
            ],
            VerificationCommands = ["cmd /c test.cmd"]
        };

        var preview = new WorkerPlanPreviewBuilder().Build(plan, root);

        Assert.Equal(WorkerPlanApprovalState.NeedsApproval, preview.ApprovalState);
        Assert.True(preview.Validation.IsValid);
        Assert.True(preview.Validation.RequiresApproval);
        Assert.Equal(WorkerPlanRiskLevel.High, preview.ApprovalSummary.RiskLevel);
        Assert.Contains("Approval required", preview.DecisionSummary);
    }

    [Fact]
    public void WorkerPlanPreviewBuilder_MarksInvalidPlanBlocked()
    {
        var root = CreateTempDirectory();
        var outside = Path.Combine(Path.GetTempPath(), $"agentq-plan-{Guid.NewGuid():N}.cs");
        var plan = new WorkerPlan
        {
            Goal = "Write outside",
            Steps =
            [
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.CreateFile,
                    Path = outside
                }
            ],
            VerificationCommands = ["Remove-Item -Recurse ."]
        };

        var preview = new WorkerPlanPreviewBuilder().Build(plan, root);

        Assert.Equal(WorkerPlanApprovalState.Blocked, preview.ApprovalState);
        Assert.False(preview.Validation.IsValid);
        Assert.Contains(preview.Validation.Issues, issue => issue.Severity == WorkerPlanValidationSeverity.Blocker);
        Assert.Contains("Blocked", preview.DecisionSummary);
    }

    [Fact]
    public void AgentPlanWorkerPlanAdapter_ExtractsFilesRiskAndVerification()
    {
        var plan = new AgentPlanWorkerPlanAdapter().Convert(
            [
                new AgentPlanItem
                {
                    Order = 1,
                    Title = "Modify AuthService.cs",
                    Detail = "Update login behavior."
                },
                new AgentPlanItem
                {
                    Order = 2,
                    Title = "Add db/migrations/001_login.sql",
                    Detail = "Create auth session table and verify with Playwright."
                }
            ],
            "Add login",
            ["npm run test:e2e", "dotnet test"]);

        Assert.Contains(plan.Steps, step => step.Path == "AuthService.cs" &&
                                           step.RequiresApproval);
        Assert.Contains(plan.Steps, step => step.Path == "db/migrations/001_login.sql" &&
                                           step.RequiresApproval);
        Assert.Contains(plan.VerificationCommands, command => command == "npm run test:e2e");
        Assert.Contains(plan.Risks, risk => risk.Contains("high-risk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MainViewModel_SetPlanApprovalPreview_RequiresApprovalForHighRiskPlan()
    {
        var viewModel = new MainViewModel();
        var preview = new WorkerPlanPreviewBuilder().Build(
            new WorkerPlan
            {
                Goal = "Add auth migration",
                Steps =
                [
                    new WorkerPlanStep
                    {
                        Kind = WorkerPlanStepKind.CreateFile,
                        Path = "db/migrations/001_auth.sql",
                        ExpectedChange = "Add auth session table.",
                        RequiresApproval = true
                    }
                ],
                VerificationCommands = ["cmd /c test.cmd"]
            },
            CreateTempDirectory());

        viewModel.SetPlanApprovalPreview(preview);

        Assert.True(viewModel.HasPendingPlanApproval);
        Assert.True(viewModel.CanApprovePlan);
        Assert.Contains("Plan approval required", viewModel.PlanApprovalStateText);
        Assert.Contains("db/migrations/001_auth.sql", viewModel.PlanApprovalPreviewText);

        viewModel.ApprovePlan();

        Assert.False(viewModel.HasPendingPlanApproval);
        Assert.Equal("Plan approved", viewModel.PlanApprovalStateText);
    }

    [Fact]
    public void DesktopPlanApprovalPreviewService_AttachesWorkerExecutionContext()
    {
        var viewModel = new MainViewModel
        {
            WorkspaceRoot = CreateTempDirectory(),
            InputText = "Add login"
        };
        viewModel.PlanItems.Add(new AgentPlanItem
        {
            Order = 1,
            Title = "Add db/migrations/001_login.sql",
            Detail = "Create login table."
        });
        var service = new DesktopPlanApprovalPreviewService(
            new AgentPlanWorkerPlanAdapter(),
            new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard()));

        service.ApplyPreview(viewModel);

        Assert.NotNull(viewModel.CurrentWorkerExecutionContext);
        Assert.Equal(WorkerExecutionState.AwaitingApproval, viewModel.CurrentWorkerExecutionContext.State);
        Assert.True(viewModel.HasPendingPlanApproval);
        Assert.Contains("Plan approval required", viewModel.PlanApprovalStateText);
    }

    [Fact]
    public void DesktopPlanCheckpointWorkflowService_BlocksUntilWorkerPlanApproved()
    {
        var viewModel = new MainViewModel
        {
            WorkspaceRoot = CreateTempDirectory(),
            CurrentWorkerExecutionContext = new WorkerExecutionContext
            {
                Plan = new WorkerPlan(),
                Preview = new WorkerPlanPreview(),
                State = WorkerExecutionState.AwaitingApproval
            }
        };
        viewModel.PlanItems.Add(new AgentPlanItem
        {
            Order = 1,
            Title = "Fix UI"
        });
        viewModel.SelectedPlanItem = viewModel.PlanItems[0];
        var service = new DesktopPlanCheckpointWorkflowService(
            new DesktopPlanWorkflowService(),
            new DesktopCheckpointWorkflowService(new AgentCheckpointService(), new DesktopGitService()),
            new DesktopPlanApprovalPreviewService(
                new AgentPlanWorkerPlanAdapter(),
                new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard())));

        Assert.Null(service.PrepareNextPlanItem(viewModel));
        Assert.Equal("Plan approval required before execution", viewModel.StatusText);

        viewModel.CurrentWorkerExecutionContext.State = WorkerExecutionState.Ready;
        Assert.NotNull(service.PrepareNextPlanItem(viewModel));
        Assert.Equal(AgentPlanItemStatus.InProgress, viewModel.PlanItems[0].Status);
    }

    [Fact]
    public void WorkerExecutionPipeline_BeginsWithApprovalAndBuildsVerificationPlans()
    {
        var root = CreateTempDirectory();
        var plan = new WorkerPlan
        {
            Goal = "Add auth migration",
            Language = "sql",
            Framework = "PostgreSQL",
            Steps =
            [
                new WorkerPlanStep
                {
                    Kind = WorkerPlanStepKind.CreateFile,
                    Path = "db/migrations/001_auth.sql",
                    ExpectedChange = "Add auth table.",
                    RequiresApproval = true
                }
            ],
            VerificationCommands = ["cmd /c test.cmd"]
        };

        var context = new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard())
            .Begin(plan, root);

        Assert.Equal(WorkerExecutionState.AwaitingApproval, context.State);
        Assert.Single(context.VerificationPlans);
        Assert.Equal("cmd /c test.cmd", context.VerificationPlans[0].Command);

        Assert.True(new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard()).Approve(context));
        Assert.Equal(WorkerExecutionState.Ready, context.State);
    }

    [Fact]
    public void WorkerExecutionPipeline_BlocksInvalidPlans()
    {
        var root = CreateTempDirectory();
        var outside = Path.Combine(Path.GetTempPath(), $"agentq-outside-{Guid.NewGuid():N}.cs");
        var context = new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard())
            .Begin(
                new WorkerPlan
                {
                    Steps =
                    [
                        new WorkerPlanStep
                        {
                            Kind = WorkerPlanStepKind.CreateFile,
                            Path = outside
                        }
                    ]
                },
                root);

        Assert.Equal(WorkerExecutionState.Blocked, context.State);
        Assert.Contains("Blocked", context.StatusMessage);
    }

    [Fact]
    public void WorkerExecutionPipeline_CreatesRepairPlanAfterFailedVerification()
    {
        var pipeline = new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard());
        var context = pipeline.Begin(
            new WorkerPlan
            {
                Goal = "Fix UI",
                Language = "typescript",
                VerificationCommands = ["npm run test:e2e"]
            },
            CreateTempDirectory());

        pipeline.ApplyVerificationResult(
            context,
            new DesktopVerificationWorkflowResult
            {
                Plan = new AgentVerificationPlan
                {
                    Title = "Worker verification",
                    Command = "npm run test:e2e"
                },
                RunResult = new VerificationRunResult
                {
                    ExitCode = 1,
                    StandardOutput = "1 failed"
                },
                FailureAnalysis = new VerificationFailureAnalysis
                {
                    Kind = VerificationFailureKind.TestFailure,
                    Title = "Tests failed",
                    Summary = "Playwright failed."
                },
                RunState = AgentRunState.Failed,
                RunStepTitle = "Verification failed",
                StatusText = "Verification failed",
                LogText = "Exit code: 1",
                FailureSummary = "Exit code: 1 - 1 failed"
            });

        Assert.Equal(WorkerExecutionState.RepairRequired, context.State);
        Assert.NotNull(context.RepairPlan);
        Assert.Equal("TestFailure", context.RepairPlan.FailureKind);
        Assert.Contains("npm run test:e2e", context.RepairPlan.VerificationCommands);
    }

    [Fact]
    public void WorkerExecutionPipeline_StopsAfterRepeatedFailures()
    {
        var pipeline = new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard());
        var context = pipeline.Begin(new WorkerPlan { Goal = "Fix tests" }, CreateTempDirectory());
        var result = new DesktopVerificationWorkflowResult
        {
            Plan = new AgentVerificationPlan
            {
                Title = "Worker verification",
                Command = "cmd /c test.cmd"
            },
            RunResult = new VerificationRunResult
            {
                ExitCode = 1,
                StandardOutput = "same failure"
            },
            FailureAnalysis = new VerificationFailureAnalysis
            {
                Kind = VerificationFailureKind.TestFailure,
                Title = "Tests failed",
                Summary = "Same assertion."
            },
            RunState = AgentRunState.Failed,
            RunStepTitle = "Verification failed",
            StatusText = "Verification failed",
            LogText = "Exit code: 1",
            FailureSummary = "Exit code: 1 - same failure"
        };

        pipeline.ApplyVerificationResult(context, result);
        pipeline.ApplyVerificationResult(context, result);
        pipeline.ApplyVerificationResult(context, result);

        Assert.Equal(WorkerExecutionState.StoppedRepeatedFailure, context.State);
        Assert.Contains("repeated 3", context.StatusMessage);
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_CreatesReactFeatureFiles()
    {
        var root = CreateTempDirectory();
        var plan = new WorkerPlanCandidateBuilder().BuildCandidate(
            "Create dashboard",
            "typescript",
            "React",
            new WorkerScaffoldRecommendation
            {
                Name = "React application feature",
                Description = "Create React feature.",
                Files =
                [
                    "src/features/<feature>/<Feature>View.tsx",
                    "src/features/<feature>/use<Feature>.ts",
                    "src/features/<feature>/<Feature>.test.ts"
                ],
                VerificationCommands = ["npm test"]
            });

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "User Dashboard"
            });

        Assert.True(result.Succeeded);
        Assert.Contains("src/features/user-dashboard/UserDashboardView.tsx", result.CreatedFiles);
        Assert.Contains("npm test", result.VerificationCommands);
        var view = await File.ReadAllTextAsync(Path.Combine(root, "src", "features", "user-dashboard", "UserDashboardView.tsx"));
        var hook = await File.ReadAllTextAsync(Path.Combine(root, "src", "features", "user-dashboard", "useUserDashboard.ts"));
        Assert.Contains("export function UserDashboardView", view);
        Assert.Contains("export function useUserDashboard", hook);
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_CreatesJavaScriptReactFeatureFiles()
    {
        var root = CreateTempDirectory();
        var plan = new WorkerPlanCandidateBuilder().BuildCandidate(
            "Create portfolio homepage",
            "javascript",
            "React",
            new WorkerScaffoldRecommendation
            {
                Name = "React JavaScript feature",
                Description = "Create React feature in JavaScript.",
                Files =
                [
                    "src/features/<feature>/<Feature>View.jsx",
                    "src/features/<feature>/use<Feature>.js",
                    "src/features/<feature>/<Feature>.test.js"
                ],
                VerificationCommands = ["npm test"]
            });

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "Portfolio Home"
            });

        Assert.True(result.Succeeded);
        Assert.Contains("src/features/portfolio-home/PortfolioHomeView.jsx", result.CreatedFiles);
        Assert.Contains("src/features/portfolio-home/usePortfolioHome.js", result.CreatedFiles);
        Assert.DoesNotContain(result.CreatedFiles, file => file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                                                           file.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase));
        var view = await File.ReadAllTextAsync(Path.Combine(root, "src", "features", "portfolio-home", "PortfolioHomeView.jsx"));
        var hook = await File.ReadAllTextAsync(Path.Combine(root, "src", "features", "portfolio-home", "usePortfolioHome.js"));
        Assert.Contains("export function PortfolioHomeView", view);
        Assert.Contains("export function usePortfolioHome", hook);
        Assert.DoesNotContain("interface", hook, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_CreatesViteReactProjectFiles()
    {
        var root = CreateTempDirectory();
        var plan = new WorkerPlanCandidateBuilder().BuildCandidate(
            "Create client app",
            "typescript",
            "Vite React",
            new WorkerScaffoldRecommendation
            {
                Name = "Vite React TypeScript project",
                Description = "Create a runnable React/Vite starter project.",
                Files =
                [
                    "package.json",
                    "index.html",
                    "vite.config.ts",
                    "tsconfig.json",
                    "src/main.tsx",
                    "src/App.tsx",
                    "src/styles.css"
                ],
                VerificationCommands = ["npm install", "npm run build"]
            });

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "Client App"
            });

        Assert.True(result.Succeeded);
        Assert.Contains("package.json", result.CreatedFiles);
        Assert.Contains("src/main.tsx", result.CreatedFiles);
        Assert.Contains("src/App.tsx", result.CreatedFiles);
        Assert.Contains("npm run build", result.VerificationCommands);
        var packageJson = await File.ReadAllTextAsync(Path.Combine(root, "package.json"));
        var app = await File.ReadAllTextAsync(Path.Combine(root, "src", "App.tsx"));
        var main = await File.ReadAllTextAsync(Path.Combine(root, "src", "main.tsx"));
        Assert.Contains("\"dev\": \"vite --host 127.0.0.1\"", packageJson);
        Assert.Contains("export function App()", app);
        Assert.Contains("createRoot", main);
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_CreatesPythonFastApiFeatureFiles()
    {
        var root = CreateTempDirectory();
        var plan = new WorkerPlanCandidateBuilder().BuildCandidate(
            "Create billing API",
            "python",
            "FastAPI",
            new WorkerScaffoldRecommendation
            {
                Name = "FastAPI service feature",
                Description = "Create FastAPI route and tests.",
                Files =
                [
                    "app/<feature_snake>.py",
                    "tests/test_<feature_snake>.py"
                ],
                VerificationCommands = ["python -m pytest"]
            });

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "Billing Portal"
            });

        Assert.True(result.Succeeded);
        Assert.Contains("app/billing_portal.py", result.CreatedFiles);
        var module = await File.ReadAllTextAsync(Path.Combine(root, "app", "billing_portal.py"));
        Assert.Contains("APIRouter", module);
        Assert.Contains("get_billing_portal", module);
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_BlocksWorkspaceEscapeAndSkipsExistingFiles()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Existing.ts"), "keep");
        var plan = new WorkerPlan
        {
            Language = "typescript",
            Framework = "React",
            Steps =
            [
                new WorkerPlanStep { Kind = WorkerPlanStepKind.CreateFile, Path = "src/Existing.ts" },
                new WorkerPlanStep { Kind = WorkerPlanStepKind.CreateFile, Path = "../escape.ts" }
            ]
        };

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "Unsafe"
            });

        Assert.False(result.Succeeded);
        Assert.Contains("src/Existing.ts", result.SkippedFiles);
        Assert.Contains(result.Issues, issue => issue.Contains("escapes workspace", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(root, "src", "Existing.ts")));
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_FailsWhenOnlySkippedFilesRemain()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Existing.ts"), "keep");
        var plan = new WorkerPlan
        {
            Language = "typescript",
            Framework = "React",
            Steps =
            [
                new WorkerPlanStep { Kind = WorkerPlanStepKind.CreateFile, Path = "src/Existing.ts" }
            ]
        };

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "Existing"
            });

        Assert.False(result.Succeeded);
        Assert.Contains("src/Existing.ts", result.SkippedFiles);
        Assert.Contains(result.Issues, issue => issue.Contains("No scaffold changes were applied", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(root, "src", "Existing.ts")));
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_UsesDetectedReactLayoutAndTestRunner()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src", "components"));
        await File.WriteAllTextAsync(Path.Combine(root, "package.json"), """{"devDependencies":{"jest":"latest"}}""");
        var plan = new WorkerPlan
        {
            Goal = "Reports",
            Language = "typescript",
            Framework = "React",
            Steps =
            [
                new WorkerPlanStep { Kind = WorkerPlanStepKind.CreateFile, Path = "<feature_dir>/<Feature>View.tsx" },
                new WorkerPlanStep { Kind = WorkerPlanStepKind.CreateFile, Path = "<feature_dir>/<Feature><ts_test_suffix>.tsx" }
            ]
        };

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "Reports"
            });

        Assert.True(result.Succeeded);
        Assert.Contains("src/components/reports/ReportsView.tsx", result.CreatedFiles);
        Assert.Contains("src/components/reports/Reports.spec.tsx", result.CreatedFiles);
        var test = await File.ReadAllTextAsync(Path.Combine(root, "src", "components", "reports", "Reports.spec.tsx"));
        Assert.Contains("@jest/globals", test);
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_UsesDetectedPythonRouterAndTestRoots()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "service", "api"));
        Directory.CreateDirectory(Path.Combine(root, "test"));
        var context = new WorkerScaffoldContext
        {
            PythonAppRoot = "service",
            PythonRouterRoot = "service/api",
            TestRoot = "test"
        };
        var plan = new WorkerPlan
        {
            Goal = "Billing",
            Language = "python",
            Framework = "FastAPI",
            Steps =
            [
                new WorkerPlanStep { Kind = WorkerPlanStepKind.CreateFile, Path = "<python_router>/<feature_snake>.py" },
                new WorkerPlanStep { Kind = WorkerPlanStepKind.CreateFile, Path = "<test_root>/test_<feature_snake>.py" }
            ]
        };

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "Billing",
                ScaffoldContext = context,
                EnableAutoWiring = false
            });

        Assert.True(result.Succeeded);
        Assert.Contains("service/api/billing.py", result.CreatedFiles);
        Assert.Contains("test/test_billing.py", result.CreatedFiles);
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_WiresReactFeatureIndex()
    {
        var root = CreateTempDirectory();
        var plan = new WorkerPlan
        {
            Goal = "Reports",
            Language = "typescript",
            Framework = "React",
            Steps =
            [
                new WorkerPlanStep { Kind = WorkerPlanStepKind.CreateFile, Path = "<feature_dir>/<Feature>View.tsx" }
            ]
        };

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "Reports"
            });

        Assert.True(result.Succeeded);
        Assert.Contains("src/features/reports/index.ts", result.WiredFiles);
        var wiring = Assert.Single(result.WiringChanges);
        Assert.Equal("src/features/reports/index.ts", wiring.Path);
        Assert.Contains("Export ReportsView", wiring.Summary);
        Assert.Contains("export { ReportsView }", wiring.After);
        var index = await File.ReadAllTextAsync(Path.Combine(root, "src", "features", "reports", "index.ts"));
        Assert.Contains("export { ReportsView } from \"./ReportsView\";", index);
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_WiresFastApiRouterIntoMain()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "app"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "app", "main.py"),
            "from fastapi import FastAPI\n\napp = FastAPI()\n");
        var plan = new WorkerPlan
        {
            Goal = "Billing",
            Language = "python",
            Framework = "FastAPI",
            Steps =
            [
                new WorkerPlanStep { Kind = WorkerPlanStepKind.CreateFile, Path = "<python_router>/<feature_snake>.py" }
            ]
        };

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "Billing"
            });

        Assert.True(result.Succeeded);
        Assert.Contains("app/main.py", result.WiredFiles);
        Assert.Contains(result.WiringChanges, change => change.Path == "app/main.py" &&
                                                        change.Before.Contains("app = FastAPI()", StringComparison.Ordinal) &&
                                                        change.After.Contains("app.include_router(billing_router)", StringComparison.Ordinal));
        var main = await File.ReadAllTextAsync(Path.Combine(root, "app", "main.py"));
        Assert.Contains("from app.routers.billing import router as billing_router", main);
        Assert.Contains("app.include_router(billing_router)", main);
    }

    [Fact]
    public async Task WorkerScaffoldExecutor_WiresRustModuleIntoLib()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "lib.rs"), "pub fn existing() {}\n");
        var plan = new WorkerPlan
        {
            Goal = "Billing",
            Language = "rust",
            Framework = "Cargo",
            Steps =
            [
                new WorkerPlanStep { Kind = WorkerPlanStepKind.CreateFile, Path = "src/<feature_snake>.rs" }
            ]
        };

        var result = await new WorkerScaffoldExecutor().ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                WorkspaceRoot = root,
                Plan = plan,
                FeatureName = "Billing"
            });

        Assert.True(result.Succeeded);
        Assert.Contains("src/lib.rs", result.WiredFiles);
        Assert.Contains(result.WiringChanges, change => change.Path == "src/lib.rs" &&
                                                        change.After.Contains("pub mod billing;", StringComparison.Ordinal));
        var lib = await File.ReadAllTextAsync(Path.Combine(root, "src", "lib.rs"));
        Assert.Contains("pub mod billing;", lib);
    }

    [Fact]
    public async Task WorkerExecutionPipeline_ExecutesScaffoldAndUpdatesContext()
    {
        var root = CreateTempDirectory();
        var pipeline = new WorkerExecutionPipeline(
            new WorkerPlanPreviewBuilder(),
            new AutoFixLoopGuard(),
            new WorkerScaffoldExecutor());
        var context = pipeline.Begin(
            new WorkerPlan
            {
                Goal = "Profile page",
                Language = "typescript",
                Framework = "React",
                Steps =
                [
                    new WorkerPlanStep
                    {
                        Kind = WorkerPlanStepKind.CreateFile,
                        Path = "src/features/<feature>/<Feature>View.tsx"
                    }
                ],
                VerificationCommands = ["npm test"]
            },
            root);

        var result = await pipeline.ExecuteScaffoldAsync(context, root, "Profile Page");

        Assert.True(result.Succeeded);
        Assert.Equal(WorkerExecutionState.ScaffoldExecuted, context.State);
        Assert.Contains("src/features/profile-page/ProfilePageView.tsx", result.CreatedFiles);
        Assert.True(File.Exists(Path.Combine(root, "src", "features", "profile-page", "ProfilePageView.tsx")));
    }

    [Fact]
    public async Task DesktopPlanCommandService_ExecutesWorkerScaffoldAndRegistersVerification()
    {
        var root = CreateTempDirectory();
        var viewModel = new MainViewModel
        {
            WorkspaceRoot = root,
            CurrentWorkerExecutionContext = new WorkerExecutionPipeline(
                    new WorkerPlanPreviewBuilder(),
                    new AutoFixLoopGuard(),
                    new WorkerScaffoldExecutor())
                .Begin(
                    new WorkerPlan
                    {
                        Goal = "Search page",
                        Language = "typescript",
                        Framework = "React",
                        Steps =
                        [
                            new WorkerPlanStep
                            {
                                Kind = WorkerPlanStepKind.CreateFile,
                                Path = "src/features/<feature>/<Feature>View.tsx"
                            }
                        ],
                        VerificationCommands = ["npm test"]
                    },
                    root)
        };
        viewModel.SetWorkerExecutionContext(viewModel.CurrentWorkerExecutionContext);
        var service = new DesktopPlanCommandService(
            new DesktopPlanCheckpointWorkflowService(
                new DesktopPlanWorkflowService(),
                new DesktopCheckpointWorkflowService(new AgentCheckpointService(), new DesktopGitService()),
                new DesktopPlanApprovalPreviewService(
                    new AgentPlanWorkerPlanAdapter(),
                    new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard(), new WorkerScaffoldExecutor()))),
            new DesktopWorkspaceContextWorkflowService(
                new WorkspaceAnalysisService(),
                new ProjectAgentConfigService(),
                new AgentSessionSummaryService(),
                new DesktopPlanCheckpointWorkflowService(
                    new DesktopPlanWorkflowService(),
                    new DesktopCheckpointWorkflowService(new AgentCheckpointService(), new DesktopGitService()),
                    new DesktopPlanApprovalPreviewService(
                        new AgentPlanWorkerPlanAdapter(),
                        new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard(), new WorkerScaffoldExecutor()))),
                new DesktopLearningSuggestionService(),
                new DesktopPlanApprovalPreviewService(
                    new AgentPlanWorkerPlanAdapter(),
                    new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard(), new WorkerScaffoldExecutor()))),
            new WorkerExecutionPipeline(new WorkerPlanPreviewBuilder(), new AutoFixLoopGuard(), new WorkerScaffoldExecutor()));

        await service.ExecuteWorkerScaffoldAsync(viewModel);

        Assert.Equal(WorkerExecutionState.ScaffoldExecuted, viewModel.CurrentWorkerExecutionContext!.State);
        Assert.Contains(viewModel.VerificationPlans, plan => plan.Command == "npm test");
        Assert.Contains(viewModel.FileChanges, change => change.RelativePath == "src/features/search-page/SearchPageView.tsx" &&
                                                        !change.ExistedBefore);
        Assert.Contains(viewModel.FileChanges, change => change.RelativePath == "src/features/search-page/index.ts" &&
                                                        change.After.Contains("export { SearchPageView }", StringComparison.Ordinal));
        Assert.Contains(viewModel.RunSteps, step => step.Title == "Worker scaffold executed");
    }

    [Fact]
    public async Task DesktopWorkspaceContextWorkflowService_PreparesScaffoldFromWorkerRecommendation()
    {
        var root = CreateTempDirectory();
        var viewModel = new MainViewModel { WorkspaceRoot = root };
        var approvalPipeline = new WorkerExecutionPipeline(
            new WorkerPlanPreviewBuilder(),
            new AutoFixLoopGuard(),
            new WorkerScaffoldExecutor());
        var approvalPreview = new DesktopPlanApprovalPreviewService(
            new AgentPlanWorkerPlanAdapter(),
            approvalPipeline);
        var checkpointWorkflow = new DesktopPlanCheckpointWorkflowService(
            new DesktopPlanWorkflowService(),
            new DesktopCheckpointWorkflowService(new AgentCheckpointService(), new DesktopGitService()),
            approvalPreview);
        var service = new DesktopWorkspaceContextWorkflowService(
            new WorkspaceAnalysisService(),
            new ProjectAgentConfigService(),
            new AgentSessionSummaryService(),
            checkpointWorkflow,
            new DesktopLearningSuggestionService(),
            approvalPreview);

        await service.RefreshWorkspaceAnalysisAsync(viewModel, value => value);

        Assert.NotNull(viewModel.CurrentWorkerExecutionContext);
        Assert.Equal(WorkerExecutionState.Ready, viewModel.CurrentWorkerExecutionContext!.State);
        Assert.Contains(viewModel.CurrentWorkerExecutionContext.Plan.Steps, step =>
            step.Kind == WorkerPlanStepKind.CreateFile &&
            step.Path == "package.json");
        Assert.True(viewModel.CanExecuteWorkerScaffold);
    }

    [Fact]
    public async Task DesktopPlanCommandService_ExecutesWorkerScaffoldAndAppliesVerificationResult()
    {
        var root = CreateTempDirectory();
        var pipeline = new WorkerExecutionPipeline(
            new WorkerPlanPreviewBuilder(),
            new AutoFixLoopGuard(),
            new WorkerScaffoldExecutor());
        var viewModel = new MainViewModel
        {
            WorkspaceRoot = root,
            CurrentWorkerExecutionContext = pipeline.Begin(
                new WorkerPlan
                {
                    Goal = "Search page",
                    Language = "typescript",
                    Framework = "React",
                    Steps =
                    [
                        new WorkerPlanStep
                        {
                            Kind = WorkerPlanStepKind.CreateFile,
                            Path = "src/features/<feature>/<Feature>View.tsx"
                        }
                    ],
                    VerificationCommands = ["npm test"]
                },
                root)
        };
        viewModel.SetWorkerExecutionContext(viewModel.CurrentWorkerExecutionContext);
        var service = CreateDesktopPlanCommandService(pipeline);
        var verificationRan = false;

        await service.ExecuteWorkerScaffoldAndVerifyAsync(
            viewModel,
            plan =>
            {
                verificationRan = true;
                Assert.Equal("npm test", plan.Command);
                return Task.FromResult<DesktopVerificationWorkflowResult?>(new DesktopVerificationWorkflowResult
                {
                    Plan = plan,
                    RunResult = new VerificationRunResult { ExitCode = 0, StandardOutput = "ok" },
                    Succeeded = true,
                    RunState = AgentRunState.Done,
                    RunStepTitle = "Verification passed",
                    RunStepDetail = "ok",
                    StatusText = "Verification passed",
                    LogText = "ok"
                });
            });

        Assert.True(verificationRan);
        Assert.Equal(WorkerExecutionState.Succeeded, viewModel.CurrentWorkerExecutionContext!.State);
        Assert.Contains(viewModel.RunSteps, step => step.Title == "Worker scaffold verification");
    }

    [Fact]
    public async Task DesktopPlanCommandService_PreparesWorkerRepairPromptAfterFailedVerification()
    {
        var root = CreateTempDirectory();
        var pipeline = new WorkerExecutionPipeline(
            new WorkerPlanPreviewBuilder(),
            new AutoFixLoopGuard(),
            new WorkerScaffoldExecutor());
        var viewModel = new MainViewModel
        {
            WorkspaceRoot = root,
            CurrentWorkerExecutionContext = pipeline.Begin(
                new WorkerPlan
                {
                    Goal = "Search page",
                    Language = "typescript",
                    Framework = "React",
                    Steps =
                    [
                        new WorkerPlanStep
                        {
                            Kind = WorkerPlanStepKind.CreateFile,
                            Path = "src/features/<feature>/<Feature>View.tsx"
                        }
                    ],
                    VerificationCommands = ["npm test"]
                },
                root)
        };
        viewModel.SetWorkerExecutionContext(viewModel.CurrentWorkerExecutionContext);
        var service = CreateDesktopPlanCommandService(pipeline);

        await service.ExecuteWorkerScaffoldAndVerifyAsync(
            viewModel,
            plan => Task.FromResult<DesktopVerificationWorkflowResult?>(new DesktopVerificationWorkflowResult
            {
                Plan = plan,
                RunResult = new VerificationRunResult { ExitCode = 1, StandardOutput = "button hidden" },
                FailureAnalysis = new VerificationFailureAnalysis
                {
                    Kind = VerificationFailureKind.TestFailure,
                    Title = "Tests failed",
                    Summary = "The scaffolded UI test failed.",
                    SuggestedNextStep = "Inspect the generated component.",
                    Evidence = ["SearchPageView did not render the expected button."]
                },
                FailureSummary = "button hidden",
                RunState = AgentRunState.Failed,
                RunStepTitle = "Verification failed",
                RunStepDetail = "button hidden",
                StatusText = "Verification failed",
                LogText = "button hidden"
            }));

        Assert.Equal(WorkerExecutionState.RepairRequired, viewModel.CurrentWorkerExecutionContext!.State);
        Assert.NotNull(viewModel.CurrentWorkerExecutionContext.RepairPlan);
        Assert.Contains(viewModel.CurrentWorkerExecutionContext.RepairPlan!.Evidence, item =>
            item.Contains("SearchPageView did not render", StringComparison.Ordinal));
        Assert.Contains("Repair the failed worker scaffold execution.", viewModel.InputText);
        Assert.Contains("Created:", viewModel.InputText);
        Assert.Contains("Rerun verification:", viewModel.InputText);
        Assert.Contains(viewModel.RunSteps, step => step.Title == "Worker repair prompt prepared");
    }

    [Fact]
    public async Task DesktopPlanCommandService_RunsWorkerRepairAndAppliesVerificationResult()
    {
        var root = CreateTempDirectory();
        var pipeline = new WorkerExecutionPipeline(
            new WorkerPlanPreviewBuilder(),
            new AutoFixLoopGuard(),
            new WorkerScaffoldExecutor());
        var context = pipeline.Begin(
            new WorkerPlan
            {
                Goal = "Search page",
                Language = "typescript",
                Framework = "React",
                Steps =
                [
                    new WorkerPlanStep
                    {
                        Kind = WorkerPlanStepKind.CreateFile,
                        Path = "src/features/<feature>/<Feature>View.tsx"
                    }
                ],
                VerificationCommands = ["npm test"]
            },
            root);
        context.State = WorkerExecutionState.RepairRequired;
        context.RepairPlan = new WorkerRepairPlan
        {
            Goal = "Repair Search page",
            Language = "typescript",
            Framework = "React",
            Summary = "The scaffolded UI test failed.",
            FailureKind = "TestFailure",
            SuggestedNextStep = "Inspect generated UI.",
            VerificationCommands = ["npm test"],
            Evidence = ["Button was hidden."]
        };
        var viewModel = new MainViewModel
        {
            WorkspaceRoot = root,
            CurrentWorkerExecutionContext = context
        };
        viewModel.SetWorkerExecutionContext(context);
        var service = CreateDesktopPlanCommandService(pipeline);
        var sent = false;
        var verified = false;

        await service.RunWorkerRepairAsync(
            viewModel,
            _ =>
            {
                sent = true;
                Assert.Contains("Repair the failed worker scaffold execution.", viewModel.InputText);
                return Task.CompletedTask;
            },
            plan =>
            {
                verified = true;
                Assert.Equal("npm test", plan.Command);
                return Task.FromResult<DesktopVerificationWorkflowResult?>(new DesktopVerificationWorkflowResult
                {
                    Plan = plan,
                    RunResult = new VerificationRunResult { ExitCode = 0, StandardOutput = "ok" },
                    Succeeded = true,
                    RunState = AgentRunState.Done,
                    RunStepTitle = "Verification passed",
                    RunStepDetail = "ok",
                    StatusText = "Verification passed",
                    LogText = "ok"
                });
            });

        Assert.True(sent);
        Assert.True(verified);
        Assert.Equal(WorkerExecutionState.Succeeded, viewModel.CurrentWorkerExecutionContext!.State);
        Assert.Contains(viewModel.RunSteps, step => step.Title == "Worker repair started");
        Assert.Contains(viewModel.RunSteps, step => step.Title == "Worker repair verification");
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
    public void ToolPermissionPolicy_CodingAllowsReadOnlyShellInspection()
    {
        var assessment = ToolPermissionClassifier.Assess(
            "bash",
            new Dictionary<string, object?>
            {
                ["command"] = "Get-ChildItem -Path \"C:\\Users\\admin\\Desktop\\test\" -Force"
            });

        var result = ToolPermissionPolicy.Evaluate(assessment, AgentWorkMode.Coding);

        Assert.Equal(PermissionRiskLevel.SafeRead, assessment.RiskLevel);
        Assert.Equal(ToolPermissionDecision.Allow, result.Decision);
        Assert.False(result.IsBlocked);
    }

    [Fact]
    public void ToolPermissionPolicy_CodingBlocksChainedReadOnlyShellInspection()
    {
        var assessment = ToolPermissionClassifier.Assess(
            "bash",
            new Dictionary<string, object?>
            {
                ["command"] = "Get-ChildItem -Force; npm install"
            });

        var result = ToolPermissionPolicy.Evaluate(assessment, AgentWorkMode.Coding);

        Assert.Equal(PermissionRiskLevel.Network, assessment.RiskLevel);
        Assert.Equal(ToolPermissionDecision.Block, result.Decision);
        Assert.True(result.IsBlocked);
    }

    [Fact]
    public void DesktopPermissionEnforcer_AllowSimilarApprovesOnlyCurrentReusableRisk()
    {
        var approvals = DesktopPermissionEnforcer.GetReusableApprovals(
            PermissionApprovalChoice.AllowSimilarForRun,
            PermissionRiskLevel.ProjectWrite);

        Assert.Equal([PermissionRiskLevel.ProjectWrite], approvals);
    }

    [Fact]
    public void DesktopPermissionEnforcer_AllowAllApprovesEditsAndVerificationForRun()
    {
        var approvals = DesktopPermissionEnforcer.GetReusableApprovals(
            PermissionApprovalChoice.AllowAllForRun,
            PermissionRiskLevel.ProjectWrite);

        Assert.Equal(
            [PermissionRiskLevel.ProjectWrite, PermissionRiskLevel.VerificationCommand],
            approvals);
    }

    [Fact]
    public void DesktopPermissionEnforcer_FormatsReusableApprovalStatus()
    {
        var status = DesktopPermissionEnforcer.FormatApprovedForRun(
            [PermissionRiskLevel.ProjectWrite, PermissionRiskLevel.VerificationCommand]);

        Assert.Equal("Run permissions: workspace edits, build/test", status);
    }

    [Fact]
    public void DesktopPermissionEnforcer_FormatsPermissionEventsForHistory()
    {
        var permissionEvent = new DesktopPermissionEvent(
            "Approved",
            "edit_file",
            PermissionRiskLevel.ProjectWrite,
            "Edit file",
            "Assets/Scripts/UI/ClickHandler.cs",
            PermissionApprovalChoice.AllowSimilarForRun);

        var text = DesktopPermissionEnforcer.FormatPermissionEvent(permissionEvent);

        Assert.Contains("Approved", text, StringComparison.Ordinal);
        Assert.Contains("AllowSimilarForRun", text, StringComparison.Ordinal);
        Assert.Contains("ProjectWrite", text, StringComparison.Ordinal);
        Assert.Contains("edit_file", text, StringComparison.Ordinal);
        Assert.Contains("Assets/Scripts/UI/ClickHandler.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopPermissionEnforcer_BuildsReadablePermissionSummary()
    {
        var summary = DesktopPermissionEnforcer.BuildPermissionSummary(new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.VerificationCommand,
            Operation = "Verification command",
            Target = "dotnet build",
            Reason = "This appears to build or test the selected project."
        }, useKoreanUi: true);

        Assert.Contains("\uBE4C\uB4DC", summary, StringComparison.Ordinal);
        Assert.Contains("\uD14C\uC2A4\uD2B8", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopPermissionEnforcer_BuildsEnglishPermissionSummary()
    {
        var summary = DesktopPermissionEnforcer.BuildPermissionSummary(new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.ProjectWrite,
            Operation = "Edit file",
            Target = "README.md",
            Reason = "This will modify a file inside the selected workspace."
        }, useKoreanUi: false);

        Assert.Equal("AgentQ wants to modify a project file.", summary);
    }

    [Fact]
    public void DesktopPermissionEnforcer_BuildsLocalizedBlockedPermissionMessage()
    {
        var assessment = new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.Destructive,
            Operation = "Shell command",
            Target = "Remove-Item -Recurse",
            Reason = "This command matches a destructive shell pattern."
        };

        var message = DesktopPermissionEnforcer.BuildPermissionBlockedMessage(
            assessment,
            AgentWorkMode.Coding,
            "Destructive commands are blocked by desktop policy.",
            useKoreanUi: true);

        Assert.Contains("AgentQ \uC548\uC804 \uC815\uCC45", message, StringComparison.Ordinal);
        Assert.Contains("\uC704\uD5D8\uB3C4: Destructive", message, StringComparison.Ordinal);
        Assert.Contains("\uC815\uCC45: Destructive commands are blocked by desktop policy.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopPermissionEnforcer_BuildsEnglishBlockedPermissionMessage()
    {
        var assessment = new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.Destructive,
            Operation = "Shell command",
            Target = "Remove-Item -Recurse",
            Reason = "This command matches a destructive shell pattern."
        };

        var message = DesktopPermissionEnforcer.BuildPermissionBlockedMessage(
            assessment,
            AgentWorkMode.Coding,
            "Destructive commands are blocked by desktop policy.",
            useKoreanUi: false);

        Assert.Contains("Blocked by AgentQ safety policy.", message, StringComparison.Ordinal);
        Assert.Contains("Risk: Destructive", message, StringComparison.Ordinal);
        Assert.Contains("Policy: Destructive commands are blocked by desktop policy.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void PermissionDialogContent_SeparatesSummaryFromRawInput()
    {
        var content = new PermissionDialogContent(
            "AgentQ\uAC00 \uD504\uB85C\uC81D\uD2B8 \uD30C\uC77C\uC744 \uC218\uC815\uD558\uB824\uACE0 \uD569\uB2C8\uB2E4.",
            "ProjectWrite / Edit file",
            "Assets/Scripts/UI/ClickHandler.cs",
            "This will modify a file inside the selected workspace.",
            "Coding mode allows workspace file edits with explicit user approval.",
            "edit_file",
            "Edit a file by replacing a specific string with a new string",
            "Path: Assets/Scripts/UI/ClickHandler.cs",
            "{\"path\":\"Assets/Scripts/UI/ClickHandler.cs\"}");

        Assert.Equal("AgentQ\uAC00 \uD504\uB85C\uC81D\uD2B8 \uD30C\uC77C\uC744 \uC218\uC815\uD558\uB824\uACE0 \uD569\uB2C8\uB2E4.", content.Summary);
        Assert.Contains("ProjectWrite", content.RiskLabel, StringComparison.Ordinal);
        Assert.Contains("ClickHandler.cs", content.Target, StringComparison.Ordinal);
        Assert.StartsWith("{", content.RawInput, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_SetRunPermissionApprovals_UpdatesStatusAndResetState()
    {
        var viewModel = new MainViewModel();

        viewModel.SetRunPermissionApprovals([PermissionRiskLevel.VerificationCommand]);

        Assert.Equal("Run permissions: build/test", viewModel.RunPermissionStatusText);
        Assert.True(viewModel.CanClearRunPermissions);

        viewModel.ClearRunPermissionStatus();

        Assert.Equal("Run permissions: none", viewModel.RunPermissionStatusText);
        Assert.False(viewModel.CanClearRunPermissions);
    }

    [Fact]
    public void AgentRunStep_LocalizesTimelineTitleAndLabelForKoreanUi()
    {
        var step = new AgentRunStep
        {
            State = AgentRunState.WaitingForApproval,
            Title = "Permission: Blocked",
            Detail = "",
            UseKoreanUi = true
        };

        Assert.Equal("\uC2B9\uC778", step.TimelineLabel);
        Assert.Equal("\uAD8C\uD55C: \uCC28\uB2E8\uB428", step.DisplayTitle);
        Assert.Equal("\uCD94\uAC00 \uC138\uBD80 \uC815\uBCF4 \uC5C6\uC74C.", step.TimelineDetail);
    }

    [Fact]
    public void MainViewModel_AddRunStep_UsesCurrentUiLanguageForTimelineItems()
    {
        var viewModel = new MainViewModel
        {
            UiLanguage = "\uD55C\uAD6D\uC5B4"
        };

        viewModel.AddRunStep(AgentRunState.RunningTool, "Evidence: bash");

        var step = Assert.Single(viewModel.RunSteps);
        Assert.True(step.UseKoreanUi);
        Assert.Equal("\uADFC\uAC70: bash", step.DisplayTitle);
    }

    [Fact]
    public void AgentRunStep_LocalizesClarifyingState()
    {
        var step = new AgentRunStep
        {
            State = AgentRunState.Clarifying,
            Title = "Waiting for user answer",
            Detail = "",
            UseKoreanUi = true
        };

        Assert.Equal("\uC9C8\uBB38", step.TimelineLabel);
        Assert.Equal("\uC0AC\uC6A9\uC790 \uB2F5\uBCC0 \uB300\uAE30", step.DisplayTitle);
        Assert.Equal("\uC9C8\uBB38 \uB300\uAE30", step.StateText);
    }

    [Fact]
    public void RunSummaryViewModel_LocalizesBusyApprovalState()
    {
        var summary = new RunSummaryViewModel();

        summary.Update(
            AgentRunState.WaitingForApproval,
            "Approval needed",
            [],
            [],
            [],
            isBusy: true,
            useKoreanUi: true);

        Assert.Equal("\uC2B9\uC778 \uB300\uAE30", summary.Phase);
        Assert.Equal("\uC694\uCCAD\uB41C \uB3C4\uAD6C \uC791\uC5C5\uC744 \uAC80\uD1A0\uD558\uC138\uC694.", summary.NextAction);
        Assert.Equal("\uAC80\uC99D \uC548 \uB428", summary.VerificationStatus);
        Assert.Equal("\uBCC0\uACBD 0\uAC1C", summary.ChangedFilesText);
    }

    [Fact]
    public void RunSummaryViewModel_ShowsClarifyingNextAction()
    {
        var summary = new RunSummaryViewModel();

        summary.Update(
            AgentRunState.Clarifying,
            "Waiting for user answer",
            [],
            [],
            [],
            isBusy: false,
            useKoreanUi: true);

        Assert.Equal("\uB2F5\uBCC0 \uB300\uAE30", summary.Phase);
        Assert.Equal("AgentQ\uC758 \uC9C8\uBB38\uC5D0 \uB2F5\uD558\uBA74 \uC774\uC5B4\uC11C \uC9C4\uD589\uD569\uB2C8\uB2E4.", summary.NextAction);
        Assert.Equal("\uAC80\uC99D \uC548 \uB428", summary.VerificationStatus);
    }

    [Fact]
    public void DesktopSessionSummaryBuilder_PreservesPendingClarificationAsNextStep()
    {
        var summary = DesktopSessionSummaryBuilder.Build(
            "C:\\work",
            "Waiting for answer",
            [
                new AgentRunStep
                {
                    State = AgentRunState.Clarifying,
                    Title = "Waiting for user answer",
                    Detail = "What type of project do you want to create?"
                }
            ],
            [],
            [],
            [],
            [
                new ChatMessageViewModel
                {
                    Role = "AgentQ",
                    Content = "What type of project do you want to create?",
                    CreatedAt = DateTime.Now
                }
            ]);

        Assert.StartsWith("Waiting for answer:", summary.Title, StringComparison.Ordinal);
        var nextStep = Assert.Single(summary.NextSteps);
        Assert.Contains("Answer AgentQ's pending question", nextStep, StringComparison.Ordinal);
        Assert.Contains("What type of project", nextStep, StringComparison.Ordinal);
        Assert.Contains("Answer AgentQ's pending question", summary.DisplayText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("npm run build")]
    [InlineData("cmd /c cd frontend && npm test")]
    [InlineData("python -m pytest")]
    [InlineData("docker compose config")]
    public void ToolPermissionClassifier_DetectsFocusedVerificationCommands(string command)
    {
        var assessment = ToolPermissionClassifier.Assess(
            "bash",
            new Dictionary<string, object?>
            {
                ["command"] = command
            });

        Assert.Equal(PermissionRiskLevel.VerificationCommand, assessment.RiskLevel);
    }

    [Theory]
    [InlineData("cmd /c cd frontend && npm run build -- --watch", "npm run build")]
    [InlineData("powershell -Command cd app; dotnet test csharp\\AgentQ.Tests\\AgentQ.Tests.csproj --filter Smoke", "dotnet test")]
    [InlineData("git push origin main", "git push")]
    [InlineData("docker compose config --quiet", "docker compose config")]
    public void ToolPermissionClassifier_AddsHumanCommandLabelToShellTargets(string command, string expectedLabel)
    {
        var target = ToolPermissionClassifier.BuildShellCommandTarget(command);

        Assert.StartsWith(expectedLabel, target, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(command, target, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("git clean -xfd")]
    [InlineData("git restore .")]
    [InlineData("git restore --source HEAD -- .")]
    [InlineData("git checkout -- .")]
    [InlineData("git checkout -f")]
    [InlineData("Remove-Item . -Recurse -Force")]
    [InlineData("powershell -EncodedCommand AAAA")]
    public void ToolPermissionPolicy_BlocksDestructiveRecoveryCommands(string command)
    {
        var assessment = ToolPermissionClassifier.Assess(
            "bash",
            new Dictionary<string, object?>
            {
                ["command"] = command
            });

        var result = ToolPermissionPolicy.Evaluate(assessment, AgentWorkMode.FullAgent);

        Assert.Equal(PermissionRiskLevel.Destructive, assessment.RiskLevel);
        Assert.Equal(ToolPermissionDecision.Block, result.Decision);
        Assert.Contains("blocked", result.PolicyReason, StringComparison.OrdinalIgnoreCase);
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

    private sealed class CapturingLlmProvider(string responseText) : ILlmProvider
    {
        public string Name => "capturing";

        public string DefaultModel => "vision-test";

        public ChatContext? LastContext { get; private set; }

        public Task<ChatResponse> GenerateResponseAsync(
            ChatContext context,
            IEnumerable<ToolDefinition> tools,
            CancellationToken ct = default)
        {
            LastContext = context;
            return Task.FromResult(new ChatResponse
            {
                Model = DefaultModel,
                Content = [ChatContent.CreateText(responseText)]
            });
        }

        public async IAsyncEnumerable<StreamChunk> GenerateStreamAsync(
            ChatContext context,
            IEnumerable<ToolDefinition> tools,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class CapturingLlmProviderFactory(ILlmProvider provider) : IDesktopLlmProviderFactory
    {
        public ILlmProvider CreateProvider(ProviderConfiguration config) => provider;
    }

    private static DesktopPlanCommandService CreateDesktopPlanCommandService(WorkerExecutionPipeline pipeline)
    {
        var approvalPipeline = new WorkerExecutionPipeline(
            new WorkerPlanPreviewBuilder(),
            new AutoFixLoopGuard(),
            new WorkerScaffoldExecutor());
        var checkpointWorkflow = new DesktopPlanCheckpointWorkflowService(
            new DesktopPlanWorkflowService(),
            new DesktopCheckpointWorkflowService(new AgentCheckpointService(), new DesktopGitService()),
            new DesktopPlanApprovalPreviewService(
                new AgentPlanWorkerPlanAdapter(),
                approvalPipeline));

        return new DesktopPlanCommandService(
            checkpointWorkflow,
            new DesktopWorkspaceContextWorkflowService(
                new WorkspaceAnalysisService(),
                new ProjectAgentConfigService(),
                new AgentSessionSummaryService(),
                checkpointWorkflow,
                new DesktopLearningSuggestionService(),
                new DesktopPlanApprovalPreviewService(
                    new AgentPlanWorkerPlanAdapter(),
                    approvalPipeline)),
            pipeline);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SaveTestPng(
        string path,
        int width,
        int height,
        Func<int, int, (byte R, byte G, byte B)> colorAt)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = colorAt(x, y);
                var offset = (y * stride) + (x * 4);
                pixels[offset] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    [Fact]
    public void ProjectPanelViewModel_ApplyAnalysis_BuildsReadyDashboard()
    {
        var viewModel = new ProjectPanelViewModel();

        viewModel.ApplyAnalysis(new WorkspaceAnalysis
        {
            ProjectType = "C#",
            Framework = "WPF",
            GitBranch = "main",
            FileCount = 42,
            DirectoryCount = 7,
            SymbolCount = 120,
            DependencyEdgeCount = 18,
            VerificationCommands = ["dotnet test"],
            ProjectMap = ["UI Layer - csharp/AgentQ.Desktop"],
            KeySymbols = ["MainViewModel"],
            KeyDependencies = ["AgentQ.Desktop -> AgentQ.Core"],
            KeyFiles = ["csharp/AgentQ.Desktop/MainWindow.xaml"]
        });

        Assert.Equal("Ready", viewModel.HealthText);
        Assert.Equal("#37D67A", viewModel.HealthAccentBrush);
        Assert.Equal("120 symbols", viewModel.SymbolCountText);
        Assert.Equal("18 dependencies", viewModel.DependencyCountText);
        Assert.Equal("1 key files", viewModel.KeyFileCountText);
        Assert.Equal("1 commands", viewModel.VerificationCommandCountText);
        Assert.Contains("C# workspace using WPF", viewModel.DashboardSummary);
    }

    [Fact]
    public void ProjectPanelViewModel_ApplyAnalysis_FlagsPartialMapWithoutVerification()
    {
        var viewModel = new ProjectPanelViewModel();

        viewModel.ApplyAnalysis(new WorkspaceAnalysis
        {
            ProjectType = "Unknown",
            Framework = "Unknown",
            FileCount = 5,
            DirectoryCount = 1
        });

        Assert.Equal("Needs verification command", viewModel.HealthText);
        Assert.Equal("#FBBF24", viewModel.HealthAccentBrush);
        Assert.Equal("0 commands", viewModel.VerificationCommandCountText);
    }

    [Fact]
    public void ProjectPanelViewModel_ApplyAnalysis_FlagsEnvironmentWarnings()
    {
        var viewModel = new ProjectPanelViewModel();

        viewModel.ApplyAnalysis(new WorkspaceAnalysis
        {
            ProjectType = "TypeScript",
            Framework = "React",
            VerificationCommands = ["npm test"],
            ProjectMap = ["UI Layer - src"],
            KeyFiles = ["src/App.tsx"],
            Hints = ["Diagnostic Warning: 'node' is not in PATH."]
        });

        Assert.Equal("Needs environment attention", viewModel.HealthText);
        Assert.Equal("#FBBF24", viewModel.HealthAccentBrush);
    }

    [Fact]
    public void MainViewModel_PendingReviewVerification_EnablesApproveAllAndVerifyForPendingChanges()
    {
        var viewModel = new MainViewModel();
        viewModel.FileChanges.Add(new FileChangeRecord
        {
            Path = "src/App.cs",
            RelativePath = "src/App.cs"
        });

        viewModel.SetPendingReviewVerification(
            new AgentVerificationPlan
            {
                Title = "Run tests",
                Command = "dotnet test",
                Reason = "Verify fix"
            },
            changedFileCount: 1,
            nextAttempt: 2,
            maxAttempts: 3);

        Assert.True(viewModel.CanApproveAllAndVerify);
        Assert.True(viewModel.HasPendingReviewVerification);
        Assert.Contains("dotnet test", viewModel.PendingReviewVerificationText);
        Assert.Contains("2/3", viewModel.ReviewWorkflowText);
    }

    [Theory]
    [InlineData(FileChangeReviewStatus.NeedsEdit)]
    [InlineData(FileChangeReviewStatus.Reverted)]
    public void MainViewModel_PendingReviewVerification_DisablesApproveAllAndVerifyForBlockedChanges(FileChangeReviewStatus status)
    {
        var viewModel = new MainViewModel();
        var change = new FileChangeRecord
        {
            Path = "src/App.cs",
            RelativePath = "src/App.cs"
        };
        viewModel.FileChanges.Add(change);
        viewModel.SetPendingReviewVerification(
            new AgentVerificationPlan
            {
                Title = "Run tests",
                Command = "dotnet test",
                Reason = "Verify fix"
            },
            changedFileCount: 1,
            nextAttempt: 2,
            maxAttempts: 3);

        change.ReviewStatus = status;

        Assert.False(viewModel.CanApproveAllAndVerify);
    }

    [Fact]
    public void MainViewModel_ClearPendingReviewVerification_ResetsReviewWorkflow()
    {
        var viewModel = new MainViewModel();
        viewModel.FileChanges.Add(new FileChangeRecord
        {
            Path = "src/App.cs",
            RelativePath = "src/App.cs"
        });
        viewModel.SetPendingReviewVerification(
            new AgentVerificationPlan
            {
                Title = "Run tests",
                Command = "dotnet test",
                Reason = "Verify fix"
            },
            changedFileCount: 1,
            nextAttempt: 2,
            maxAttempts: 3);

        viewModel.ClearPendingReviewVerification();

        Assert.False(viewModel.HasPendingReviewVerification);
        Assert.False(viewModel.CanApproveAllAndVerify);
        Assert.Equal("No verification queued.", viewModel.PendingReviewVerificationText);
    }

    [Fact]
    public void MainViewModel_RefreshPlanEvidenceSummary_ConnectsPlanEvidenceVerificationAndEval()
    {
        var viewModel = new MainViewModel();
        var item = new AgentPlanItem
        {
            Order = 1,
            Title = "Fix parser",
            Detail = "Patch parser bug"
        };
        viewModel.PlanItems.Add(item);
        viewModel.SelectedPlanItem = item;
        viewModel.AddRunStep(AgentRunState.RunningTool, "Evidence: read_file", "Read parser.cs because it owns Parse().");
        viewModel.AddVerificationResult(new VerificationResultCard
        {
            Status = "PASSED",
            Title = "Unit tests",
            Summary = "Parser tests passed"
        });
        viewModel.ApplyEvalDashboard(new EvalReplayDashboardReport
        {
            Findings = { "No failed tools detected." }
        });

        Assert.Contains("Pending: 1. Fix parser", viewModel.PlanEvidenceStatusText);
        Assert.Contains("Evidence: read_file", viewModel.PlanEvidenceSummary);
        Assert.Contains("PASSED Unit tests", viewModel.PlanEvidenceSummary);
        Assert.Contains("No failed tools detected", viewModel.PlanEvidenceSummary);
    }

    [Fact]
    public void MainViewModel_RefreshPlanEvidenceSummary_KeepsVisualEvidenceWithLatestFileEvidence()
    {
        var viewModel = new MainViewModel();
        var item = new AgentPlanItem
        {
            Order = 1,
            Title = "Analyze Unity damage flash",
            Detail = "Use screenshot and inspect scripts."
        };
        viewModel.PlanItems.Add(item);
        viewModel.SelectedPlanItem = item;
        viewModel.AddRunStep(
            AgentRunState.GatheringContext,
            "Evidence: visual attachment",
            "image: damage-flash.png, image/png, 4 KB, dimensions 640x360.");
        viewModel.AddRunStep(
            AgentRunState.RunningTool,
            "Evidence: read_file",
            "Read DamageFlashController.cs because it controls hit feedback.");

        Assert.Contains("visual attachment", viewModel.PlanEvidenceSummary);
        Assert.Contains("damage-flash.png", viewModel.PlanEvidenceSummary);
        Assert.Contains("read_file", viewModel.PlanEvidenceSummary);
        Assert.Contains("DamageFlashController.cs", viewModel.PlanEvidenceSummary);
    }

    [Fact]
    public void MainViewModel_RefreshPlanEvidenceSummary_UsesPlanStatusAccent()
    {
        var viewModel = new MainViewModel();
        var item = new AgentPlanItem
        {
            Order = 1,
            Title = "Verify change",
            Detail = "Run focused verification",
            Status = AgentPlanItemStatus.Done
        };

        viewModel.PlanItems.Add(item);
        viewModel.SelectedPlanItem = item;

        Assert.Equal("#37D67A", viewModel.PlanEvidenceAccentBrush);
        Assert.Contains("Done: 1. Verify change", viewModel.PlanEvidenceStatusText);
    }

    [Fact]
    public void DesktopPanelViewModels_DefaultEmptyStates_GiveNextActions()
    {
        var git = new GitPanelViewModel();
        var project = new ProjectPanelViewModel();
        var eval = new EvalDashboardViewModel();
        var run = new RunSummaryViewModel();

        Assert.Contains("Click Status", git.StatusText);
        Assert.Contains("Click Diff", git.DiffText);
        Assert.Contains("Analyze", project.ProjectType);
        Assert.Contains("Analyze", project.Stats);
        Assert.Contains("Click Refresh", eval.Summary);
        Assert.Contains("Evidence will appear", run.LastEvidence);
        Assert.Contains("No timing", run.TimingText);
    }

    [Fact]
    public void DesktopPanelViewModels_LocalizeDefaultEmptyStatesForKoreanUi()
    {
        var git = new GitPanelViewModel { UseKoreanUi = true };
        var project = new ProjectPanelViewModel { UseKoreanUi = true };
        var eval = new EvalDashboardViewModel { UseKoreanUi = true };

        Assert.Contains("\uC0C1\uD0DC", git.StatusText);
        Assert.Contains("diff", git.DiffText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\uBD84\uC11D", project.ProjectType);
        Assert.Contains("\uD504\uB85C\uC81D\uD2B8", project.DashboardSummary);
        Assert.Contains("\uC0C8\uB85C\uACE0\uCE68", eval.Summary);
        Assert.Contains("\uCCAB", eval.UpdatedText);
    }

    [Fact]
    public void MainViewModel_UiLanguage_PropagatesToDesktopPanels()
    {
        var viewModel = new MainViewModel
        {
            UiLanguage = "\uD55C\uAD6D\uC5B4"
        };

        Assert.True(viewModel.Git.UseKoreanUi);
        Assert.True(viewModel.Project.UseKoreanUi);
        Assert.True(viewModel.EvalDashboard.UseKoreanUi);
        Assert.Contains("\uC0C1\uD0DC", viewModel.Git.StatusText);
        Assert.Contains("\uBD84\uC11D", viewModel.Project.ProjectType);
        Assert.Contains("\uC0C8\uB85C\uACE0\uCE68", viewModel.EvalDashboard.Summary);
    }

    [Fact]
    public void MainViewModel_UiLanguageChange_LocalizesEmptyPanelsWithoutOverwritingLoadedPanelData()
    {
        var viewModel = new MainViewModel();
        viewModel.Git.StatusText = "## main...origin/main";
        viewModel.Project.ProjectType = "Unity";
        viewModel.EvalDashboard.Summary = "Replay: 3 tools";

        viewModel.UiLanguage = "\uD55C\uAD6D\uC5B4";

        Assert.Equal("## main...origin/main", viewModel.Git.StatusText);
        Assert.Equal("Unity", viewModel.Project.ProjectType);
        Assert.Equal("Replay: 3 tools", viewModel.EvalDashboard.Summary);
        Assert.Contains("\uD604\uC7AC", viewModel.Git.DiffText);
        Assert.Contains("\uBD84\uC11D", viewModel.Project.Stats);
        Assert.Contains("\uCCAB", viewModel.EvalDashboard.UpdatedText);
    }

    [Fact]
    public void DesktopGitPanelWorkflowService_LocalizesDynamicGitStatusForKoreanUi()
    {
        var service = new DesktopGitPanelWorkflowService(new DesktopGitService());
        var viewModel = new MainViewModel
        {
            UiLanguage = "\uD55C\uAD6D\uC5B4"
        };

        service.SetSelectedReviewStatus(viewModel, GitChangeReviewStatus.Approved);

        Assert.Contains("\uC120\uD0DD\uB41C \uBCC0\uACBD \uD30C\uC77C", viewModel.StatusText);

        service.ApplySnapshot(
            viewModel,
            new DesktopGitSnapshot
            {
                Status = new GitCommandResult
                {
                    ExitCode = 0,
                    StandardOutput = "## main"
                },
                DiffStat = new GitCommandResult
                {
                    ExitCode = 0
                },
                FullDiff = new GitCommandResult
                {
                    ExitCode = 0
                },
                ChangedFiles = []
            });

        Assert.Equal("\uBCC0\uACBD \uD30C\uC77C\uC774 \uC5C6\uC2B5\uB2C8\uB2E4.", viewModel.GitSelectedFileDiffText);
        Assert.StartsWith("\uB9C8\uC9C0\uB9C9 \uC5C5\uB370\uC774\uD2B8:", viewModel.GitLastUpdatedText);
    }

    [Fact]
    public void DesktopLocalizer_FormatsGitWorkflowMessages()
    {
        Assert.Equal("Pull blocked: local changes", DesktopLocalizer.FormatUiText(DesktopText.GitPullBlocked, useKoreanUi: false, "local changes"));
        Assert.Equal("Pull \uCC28\uB2E8\uB428: \uB85C\uCEEC \uBCC0\uACBD", DesktopLocalizer.FormatUiText(DesktopText.GitPullBlocked, useKoreanUi: true, "\uB85C\uCEEC \uBCC0\uACBD"));
        Assert.Equal("\uBC31\uC5C5 \uBE0C\uB79C\uCE58 \uC0DD\uC131\uB428: backup/main", DesktopLocalizer.FormatUiText(DesktopText.GitBackupBranchCreated, useKoreanUi: true, "backup/main"));
    }

    [Fact]
    public void ProjectPanel_ExposesVSCodeOpenAction()
    {
        var xaml = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "Views", "ProjectPanel.xaml"));
        var codeBehind = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "Views", "ProjectPanel.xaml.cs"));
        var callbacks = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "Services", "DesktopPanelEventBinder.cs"));

        Assert.Contains("OpenVSCodeText", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenWorkspaceInVSCode_OnClick", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenWorkspaceInVSCodeRequested", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenWorkspaceInVSCode", callbacks, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopWorkspaceCommandService_CreatesVSCodeStartInfoForWorkspace()
    {
        var startInfo = DesktopWorkspaceCommandService.CreateVSCodeStartInfo(@"C:\Users\admin\Gun Clicker");

        Assert.Equal("code.cmd", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(@"C:\Users\admin\Gun Clicker", Assert.Single(startInfo.ArgumentList));
    }

    [Fact]
    public void FileChangeRecord_ExposesAfterTextForSidePreview()
    {
        var change = new FileChangeRecord
        {
            Path = @"C:\repo\src\App.cs",
            RelativePath = "src/App.cs",
            Before = "old",
            After = "new code"
        };

        Assert.Equal("new code", change.SourcePreviewText);
    }

    [Fact]
    public void FileChangeReviewPanel_ExposesDiffAndFilePreviewTabs()
    {
        var xaml = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "Views", "FileChangeReviewPanel.xaml"));

        Assert.Contains("Header=\"Diff\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"File\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedFileChange.SourcePreviewText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FileChangeReviewPanel_ExposesSourceBrowser()
    {
        var xaml = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "Views", "FileChangeReviewPanel.xaml"));
        var codeBehind = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "Views", "FileChangeReviewPanel.xaml.cs"));

        Assert.Contains("FileChangesList_OnSelectionChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Browse\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<TreeView", xaml, StringComparison.Ordinal);
        Assert.Contains("HierarchicalDataTemplate", xaml, StringComparison.Ordinal);
        Assert.Contains("SourceFilesTree_OnSelectedItemChanged", xaml, StringComparison.Ordinal);
        Assert.Contains("SourceFileFilter", xaml, StringComparison.Ordinal);
        Assert.Contains("SourceFiles", xaml, StringComparison.Ordinal);
        Assert.Contains("TreeDisplayName", xaml, StringComparison.Ordinal);
        Assert.Contains("DetailText", xaml, StringComparison.Ordinal);
        Assert.Contains("SourceFilePreviewText", xaml, StringComparison.Ordinal);
        Assert.Contains("RefreshSourceFilesRequested", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SelectedSourceFileChanged", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SelectedFileChangeChanged", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void CodePreviewWindow_UsesRichTextBoxForHighlightedPreview()
    {
        var xaml = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "Views", "CodePreviewWindow.xaml"));
        var mainWindow = System.IO.File.ReadAllText(FindRepoFile("csharp", "AgentQ.Desktop", "MainWindow.xaml.cs"));

        Assert.Contains("<RichTextBox", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowSelectedFileChangePreview", mainWindow, StringComparison.Ordinal);
        Assert.Contains("OpenSelectedSourceFileAsync", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopCodeHighlighter_ColorsCSharpTokens()
    {
        var document = DesktopCodeHighlighter.CreateDocument("public class Slime { // note");
        var paragraph = Assert.IsType<System.Windows.Documents.Paragraph>(Assert.Single(document.Blocks));
        var text = new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd).Text;

        Assert.Equal(5000, document.PageWidth);
        Assert.Equal(5000, document.ColumnWidth);
        Assert.Contains("public class Slime", text, StringComparison.Ordinal);
        Assert.Contains(paragraph.Inlines.OfType<System.Windows.Documents.Run>(), run =>
            run.Text == "public" && run.Foreground.ToString().Contains("569CD6", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paragraph.Inlines.OfType<System.Windows.Documents.Run>(), run =>
            run.Text == "Slime" && run.Foreground.ToString().Contains("4EC9B0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DesktopSourceBrowserService_LoadsAndOpensWorkspaceFiles()
    {
        var root = CreateTempDirectory();
        var sourceDirectory = Path.Combine(root, "src");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "App.cs"), "class App {}");
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        await File.WriteAllTextAsync(Path.Combine(root, "bin", "Ignored.cs"), "ignored");
        var viewModel = new MainViewModel
        {
            WorkspaceRoot = root,
            SourceFileFilter = "App"
        };
        var service = new DesktopSourceBrowserService();

        service.Refresh(viewModel);
        var sourceDirectoryEntry = Assert.Single(viewModel.SourceFiles, file => file.IsDirectory && file.RelativePath == "src/");
        viewModel.SelectedSourceFile = Assert.Single(sourceDirectoryEntry.Children);
        await service.OpenSelectedAsync(viewModel);

        Assert.Equal("src/App.cs", viewModel.SelectedSourceFile.RelativePath);
        Assert.Equal("  \u2022 App.cs", viewModel.SelectedSourceFile.TreeDisplayName);
        Assert.Equal("class App {}", viewModel.SourceFilePreviewText);
    }

    [Fact]
    public void RunSummaryViewModel_ShowsElapsedTimingAndStepCount()
    {
        var run = new RunSummaryViewModel();
        var started = DateTime.Now.AddSeconds(-12);
        var steps = new List<AgentRunStep>
        {
            new()
            {
                State = AgentRunState.GatheringContext,
                Title = "Context",
                CreatedAt = started
            },
            new()
            {
                State = AgentRunState.RunningTool,
                Title = "Tool",
                CreatedAt = started.AddSeconds(7)
            }
        };

        run.Update(
            AgentRunState.RunningTool,
            "Running",
            steps,
            [],
            [],
            isBusy: false);

        Assert.Contains("7s elapsed", run.TimingText);
        Assert.Contains("2 step", run.TimingText);
    }

    private sealed class StubHttpClientFactory(
        string content,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string contentType = "text/plain") : IHttpClientFactory, IDisposable
    {
        private readonly StubHttpMessageHandler _handler = new(content, statusCode, contentType);

        public HttpRequestMessage? LastRequest => _handler.LastRequest;

        public string LastRequestBody => _handler.LastRequestBody;

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }

        public void Dispose()
        {
            _handler.Dispose();
        }
    }

    private sealed class StubHttpMessageHandler(
        string content,
        HttpStatusCode statusCode,
        string contentType) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string LastRequestBody { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, contentType)
            });
        }
    }

    private sealed class FakeEmbeddingClient : IEmbeddingClient
    {
        public Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
            IReadOnlyList<string> inputs,
            string model,
            CancellationToken ct = default)
        {
            IReadOnlyList<float[]> vectors = inputs
                .Select((_, index) => new[] { (float)index, (float)(index + 1) })
                .ToList();
            return Task.FromResult(vectors);
        }
    }

    private static string ExtractMcpText(JsonElement result)
    {
        return result.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private sealed class FakeMcpClient(JsonElement callResult) : IMcpClient
    {
        public string LastToolName { get; private set; } = string.Empty;

        public JsonElement LastArguments { get; private set; }

        public Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(McpServerConfig server, CancellationToken ct = default)
        {
            IReadOnlyList<McpToolInfo> tools =
            [
                new McpToolInfo
                {
                    Name = "scene/read-object",
                    Description = "Read a scene object.",
                    InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
                }
            ];
            return Task.FromResult(tools);
        }

        public Task<JsonElement> CallToolAsync(McpServerConfig server, string toolName, JsonElement arguments, CancellationToken ct = default)
        {
            LastToolName = toolName;
            LastArguments = arguments.Clone();
            return Task.FromResult(callResult.Clone());
        }
    }
}
