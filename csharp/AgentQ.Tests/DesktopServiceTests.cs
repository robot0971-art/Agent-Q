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
    public void DesktopAgentService_SystemPrompt_PrioritizesSymbolSearchForCodeNavigation()
    {
        var field = typeof(DesktopAgentService).GetField("SystemPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        var prompt = Assert.IsType<string>(field?.GetValue(null));

        Assert.Contains("prefer symbol_search first", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hybrid_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("semantic_search", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grep_search", prompt, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("bug fix", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hybrid_search", prompt, StringComparison.OrdinalIgnoreCase);
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
            """{"compilerOptions":{"jsx":"react-jsx","target":"ES2022","module":"ESNext"}}""");
        await File.WriteAllTextAsync(
            Path.Combine(root, "frontend", "src", "pages", "Dashboard.tsx"),
            """
            import React from 'react';

            export function DashboardView() {
              return <main />;
            }

            export const useDashboard = () => [];
            """);

        var result = await new TypeScriptWorkerHost().AnalyzeAsync(root, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Packages, package => package.Path == "frontend/package.json");
        Assert.Contains(result.Tsconfigs, config => config.Path == "frontend/tsconfig.json");
        Assert.Contains(result.NpmScripts, script => script.Name == "build");
        Assert.Contains(result.Imports, import => import.Source == "react");
        Assert.Contains(result.ReactComponents, component => component.Name == "DashboardView");
        Assert.Contains(result.Routes, route => route.Path.Contains("Dashboard.tsx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Exports, export => export.Name == "useDashboard");
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
            Path.Combine(root, "frontend", "src", "App.tsx"),
            """
            export function AppShell() {
              return null;
            }
            """);

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains("React", analysis.Framework);
        Assert.Contains("Vite", analysis.Framework);
        Assert.Contains("TypeScript", analysis.Framework);
        Assert.Contains(analysis.Hints, hint => hint.Contains("TypeScript worker indexed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("component AppShell", StringComparison.OrdinalIgnoreCase));
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
            Path.Combine(root, "backend", "app", "main.py"),
            """
            from fastapi import FastAPI
            from sqlalchemy.orm import DeclarativeBase

            app = FastAPI()

            class Base(DeclarativeBase):
                pass

            class User(Base):
                __tablename__ = "users"

            @app.get("/users")
            async def list_users():
                return []
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "backend", "tests", "test_users.py"),
            """
            def test_users():
                assert True
            """);

        var result = await new PythonWorkerHost().AnalyzeAsync(root, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result.Requirements, item => item.Path == "backend/requirements.txt");
        Assert.Contains(result.Imports, item => item.Module == "fastapi");
        Assert.Contains(result.FastApiRoutes, route => route.Route == "/users" && route.Method == "GET");
        Assert.Contains(result.SqlAlchemyModels, model => model.Name == "User");
        Assert.Contains(result.PytestTargets, target => target.Path == "backend/tests/test_users.py");
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
            Path.Combine(root, "backend", "app", "main.py"),
            """
            from fastapi import FastAPI

            app = FastAPI()

            class User:
                __tablename__ = "users"

            @app.post("/users")
            def create_user():
                return {}
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "backend", "tests", "test_api.py"), "def test_api(): assert True");

        var analysis = await new WorkspaceAnalysisService().AnalyzeAsync(root, CancellationToken.None);

        Assert.Contains("FastAPI", analysis.Framework);
        Assert.Contains("SQLAlchemy", analysis.Framework);
        Assert.Contains("pytest", analysis.Framework);
        Assert.Contains(analysis.Hints, hint => hint.Contains("Python worker indexed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("route POST /users", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.KeySymbols, symbol => symbol.Contains("model User", StringComparison.OrdinalIgnoreCase));
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

    private sealed class StubHttpClientFactory(string content, HttpStatusCode statusCode = HttpStatusCode.OK) : IHttpClientFactory, IDisposable
    {
        private readonly StubHttpMessageHandler _handler = new(content, statusCode);

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

    private sealed class StubHttpMessageHandler(string content, HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string LastRequestBody { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
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
}
