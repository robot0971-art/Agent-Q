using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;
using AgentQ.Core.Providers;
using System.Net;
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
    public void DesktopEvidenceFormatter_ExplainsBroadSearch()
    {
        var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(
            "grep_search",
            new Dictionary<string, object?> { ["pattern"] = "ProjectMap" },
            "C:\\repo");

        Assert.Contains("broad workspace search", evidence);
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
        }
    }
}
