using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;
using AgentQ.Core.Providers;
using System.Net;
using System.Reflection;
using System.Text.Json;
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
    public void DesktopAgentService_SystemPrompt_PrioritizesSymbolSearchForCodeNavigation()
    {
        var field = typeof(DesktopAgentService).GetField("SystemPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        var prompt = Assert.IsType<string>(field?.GetValue(null));

        Assert.Contains("prefer symbol_search first", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hybrid_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("semantic_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grep_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirmed facts", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supporting files", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("can attempt to read HTTP/HTTPS links", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot access external websites", prompt, StringComparison.OrdinalIgnoreCase);
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
    [InlineData("로그인 오류 고쳐줘", DesktopTaskKind.BugFix)]
    [InlineData("새 설정 옵션 추가해줘", DesktopTaskKind.Feature)]
    [InlineData("이 변경사항 코드 리뷰해줘", DesktopTaskKind.CodeReview)]
    [InlineData("README 문서 고쳐줘", DesktopTaskKind.Documentation)]
    [InlineData("프로젝트 구조 분석해줘", DesktopTaskKind.Analysis)]
    [InlineData("이 서비스 구조 리팩터링하자", DesktopTaskKind.Refactor)]
    public void DesktopTaskClassifier_ClassifiesCommonTaskTypes(string text, DesktopTaskKind expected)
    {
        Assert.Equal(expected, DesktopTaskClassifier.Classify(text));
    }

    [Fact]
    public void DesktopPromptAssemblyService_AddsTaskSpecificGuidance()
    {
        var profile = DesktopPromptAssemblyService.BuildTaskProfile("로그인 오류 고쳐줘");
        var prompt = DesktopPromptAssemblyService.BuildSystemPrompt("Base prompt", profile);

        Assert.Equal(DesktopTaskKind.BugFix, profile.Kind);
        Assert.Contains("Dynamic task guidance", prompt);
        Assert.Contains("Context prioritization", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(profile.ContextHint, prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Execution strategy", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Patch the minimal root cause", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bug fix", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hybrid_search", prompt, StringComparison.OrdinalIgnoreCase);
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
        var text = "찾아보겠습니다.</think>`EmbeddingIndexBuilder.cs` 확인<think>hidden</think>완료";

        var filtered = ModelReasoningTagFilter.Strip(text);

        Assert.Equal("찾아보겠습니다.`EmbeddingIndexBuilder.cs` 확인완료", filtered);
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
              "devDependencies": { "vite": "latest", "typescript": "latest" },
              "scripts": { "build": "vite build", "test": "vitest" }
            }
            """);
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
        Assert.Contains(result.Exports, export => export.Name == "useDashboard");
        Assert.Contains(result.Exports, export => export.Name == "loadDashboard");
        Assert.Contains(result.Symbols, symbol => symbol.Name == "loadRoute");
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
              "devDependencies": { "vite": "latest", "typescript": "latest" },
              "scripts": { "build": "vite build" }
            }
            """);
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

            export function AppShell() {
              return apiClient;
            }
            """);

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains("React", analysis.Framework);
        Assert.Contains("Vite", analysis.Framework);
        Assert.Contains("TypeScript", analysis.Framework);
        Assert.Contains(analysis.Hints, hint => hint.Contains("TypeScript worker indexed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("component AppShell", StringComparison.OrdinalIgnoreCase));
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
        await File.WriteAllTextAsync(Path.Combine(root, "backend", "tests", "test_api.py"), "def test_api(): assert True");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains("FastAPI", analysis.Framework);
        Assert.Contains("SQLAlchemy", analysis.Framework);
        Assert.Contains("pytest", analysis.Framework);
        Assert.Contains(analysis.Hints, hint => hint.Contains("Python worker indexed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("route POST /users", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("model User", StringComparison.OrdinalIgnoreCase));
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
    public void VerificationCommandPolicy_BlocksUnsafeDirectoryScopedCommands()
    {
        Assert.False(VerificationCommandPolicy.IsAllowed("cmd /c cd .. && npm run build"));
        Assert.False(VerificationCommandPolicy.IsAllowed("cmd /c cd frontend & del * && npm run build"));
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
                    Args = ["unity-mcp.js"]
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
        Assert.Contains(report.Metrics, metric => metric.Contains("Replay: 2 tools", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("Telemetry: 1 events", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Metrics, metric => metric.Contains("Verification: 0 passed, 1 failed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Findings, finding => finding.Contains("Tool failure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.ReplayEntries, entry => entry.Contains("FAILED shell_execute", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.FailureFingerprints, fingerprint => fingerprint.StartsWith("failure-", StringComparison.OrdinalIgnoreCase) &&
                                                                  fingerprint.EndsWith("x2", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal("Learning candidates", new MainViewModel().LearningCandidatesText);
        Assert.Equal("File", new MainViewModel().MenuFileText);
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

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
