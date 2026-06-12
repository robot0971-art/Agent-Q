using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Desktop.Services;
using Xunit;

namespace AgentQ.Tests;

public sealed class TaskDecompositionTests
{
    [Theory]
    [InlineData("Fix a typo in README.md", TaskComplexity.Moderate)]
    [InlineData("Add a simple unit test for math", TaskComplexity.Moderate)]
    [InlineData("Add oauth support and then database integration finally check if it works", TaskComplexity.Complex)]
    [InlineData("refactor everything and configure oauth", TaskComplexity.Complex)]
    public void ComplexityEstimator_ShouldClassifyCorrectly(string userText, TaskComplexity expectedComplexity)
    {
        var complexity = DesktopTaskComplexityEstimator.EstimateComplexity(userText);
        Assert.Equal(expectedComplexity, complexity);
    }

    [Fact]
    public async Task Decomposer_ShouldParseLlmResponseToPlan()
    {
        var decomposer = new TaskDecomposer();
        var mockResponse = """
```json
{
  "Goal": "Implement OAuth and Database",
  "Steps": [
    {
      "Order": 1,
      "Description": "Setup database migrations",
      "Kind": "Implement",
      "RelevantFiles": ["db.cs"],
      "DependsOn": [],
      "VerificationCommand": "dotnet run --migrate"
    },
    {
      "Order": 2,
      "Description": "Configure OAuth endpoints",
      "Kind": "Implement",
      "RelevantFiles": ["auth.cs"],
      "DependsOn": ["1"],
      "VerificationCommand": ""
    }
  ],
  "Risks": ["Database locking"]
}
```
""";
        var provider = new TestLlmProvider(mockResponse);
        var config = new ProviderConfiguration { Model = "test-model" };
        var analysis = new WorkspaceAnalysis
        {
            ProjectType = "C#",
            Framework = ".NET 10",
            KeyFiles = new List<string> { "db.cs", "auth.cs" }
        };

        var plan = await decomposer.DecomposeAsync("Implement OAuth and Database", analysis, provider, config, CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("Implement OAuth and Database", plan.Goal);
        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal("Setup database migrations", plan.Steps[0].Description);
        Assert.Equal(TaskStepKind.Implement, plan.Steps[0].Kind);
        Assert.Single(plan.Steps[0].RelevantFiles);
        Assert.Equal("db.cs", plan.Steps[0].RelevantFiles[0]);
        Assert.Single(plan.Risks);
        Assert.Equal("Database locking", plan.Risks[0]);
    }

    [Fact]
    public async Task Decomposer_RemovesVerificationCommandsOutsideAllowlist()
    {
        var decomposer = new TaskDecomposer();
        var mockResponse = """
{
  "Goal": "Fix issue",
  "Steps": [
    {
      "Order": 1,
      "Description": "Fix the issue",
      "Kind": "Implement",
      "RelevantFiles": ["src/app.cs"],
      "DependsOn": [],
      "VerificationCommand": "Remove-Item -Recurse C:\\"
    }
  ],
  "Risks": []
}
""";
        var provider = new TestLlmProvider(mockResponse);
        var config = new ProviderConfiguration { Model = "test-model" };
        var analysis = new WorkspaceAnalysis
        {
            ProjectType = "C#",
            Framework = ".NET",
            KeyFiles = new List<string> { "src/app.cs" }
        };

        var plan = await decomposer.DecomposeAsync("Fix issue", analysis, provider, config, CancellationToken.None);

        Assert.Single(plan.Steps);
        Assert.Equal(string.Empty, plan.Steps[0].VerificationCommand);
    }

    [Fact]
    public async Task Decomposer_RemovesRelevantFilesOutsideWorkspace()
    {
        var root = Directory.CreateTempSubdirectory("agentq-task-plan-").FullName;
        var outside = Path.Combine(Path.GetTempPath(), $"agentq-outside-{System.Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "app.cs"), "class App {}");
            await File.WriteAllTextAsync(outside, "secret");
            var decomposer = new TaskDecomposer();
            var mockResponse = $$"""
{
  "Goal": "Fix issue",
  "Steps": [
    {
      "Order": 1,
      "Description": "Fix the issue",
      "Kind": "Implement",
      "RelevantFiles": ["app.cs", "{{outside.Replace("\\", "\\\\")}}", "../escape.cs"],
      "DependsOn": [],
      "VerificationCommand": ""
    }
  ],
  "Risks": []
}
""";
            var provider = new TestLlmProvider(mockResponse);
            var config = new ProviderConfiguration { Model = "test-model" };
            var analysis = new WorkspaceAnalysis
            {
                WorkspaceRoot = root,
                ProjectType = "C#",
                Framework = ".NET"
            };

            var plan = await decomposer.DecomposeAsync("Fix issue", analysis, provider, config, CancellationToken.None);

            Assert.Equal(["app.cs"], plan.Steps[0].RelevantFiles);
        }
        finally
        {
            if (File.Exists(outside))
            {
                File.Delete(outside);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TaskContextSelector_DoesNotReadFilesOutsideWorkspace()
    {
        var root = Directory.CreateTempSubdirectory("agentq-task-context-").FullName;
        var outside = Path.Combine(Path.GetTempPath(), $"agentq-outside-{System.Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "app.cs"), "class App {}");
            await File.WriteAllTextAsync(outside, "secret outside content");
            var selector = new TaskContextSelector();
            var step = new TaskStep
            {
                Description = "Inspect files",
                Kind = TaskStepKind.Investigate,
                RelevantFiles = ["app.cs", outside, "../escape.cs"]
            };

            var context = await selector.BuildTaskContextAsync(
                step,
                new WorkspaceAnalysis { WorkspaceRoot = root },
                new WorkspaceSymbolIndex(),
                root,
                CancellationToken.None);

            Assert.Contains("class App", context, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret outside content", context, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Skipped file outside workspace", context, System.StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(outside))
            {
                File.Delete(outside);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TaskContextSelector_DoesNotReadFilesThroughSymlinkedDirectory()
    {
        var root = Directory.CreateTempSubdirectory("agentq-task-context-link-").FullName;
        var outside = Directory.CreateTempSubdirectory("agentq-task-context-outside-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "app.cs"), "class App {}");
            await File.WriteAllTextAsync(Path.Combine(outside, "secret.cs"), "class OutsideSecret {}");
            var linkPath = Path.Combine(root, "linked");
            try
            {
                Directory.CreateSymbolicLink(linkPath, outside);
            }
            catch
            {
                return;
            }

            var selector = new TaskContextSelector();
            var step = new TaskStep
            {
                Description = "Inspect files",
                Kind = TaskStepKind.Investigate,
                RelevantFiles = ["app.cs", "linked/secret.cs"]
            };

            var context = await selector.BuildTaskContextAsync(
                step,
                new WorkspaceAnalysis { WorkspaceRoot = root },
                new WorkspaceSymbolIndex(),
                root,
                CancellationToken.None);

            Assert.Contains("class App", context, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OutsideSecret", context, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Skipped file outside workspace", context, System.StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(outside))
            {
                Directory.Delete(outside, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("Stopped after reaching the maximum tool steps (50).")]
    [InlineData("Coding task did not use workspace tools after retry, so AgentQ stopped this answer instead of showing an unsupported completion.")]
    [InlineData("Project scaffold creation failed: Permission denied by user")]
    [InlineData("Prepared project scaffold was not created.")]
    [InlineData("로컬 개발 서버를 띄우지 못했습니다. Permission denied.")]
    public void TaskExecutor_TreatsGuardAndDeterministicFailuresAsStepFailures(string stepOutput)
    {
        Assert.False(TaskExecutor.IsStepOutputSuccessful(stepOutput));
    }

    [Fact]
    public void TaskExecutor_TreatsNormalCompletionAsStepSuccess()
    {
        Assert.True(TaskExecutor.IsStepOutputSuccessful("Created the requested folder."));
    }

    [Fact]
    public void TaskExecutor_DropsUnsafeVerificationCommandBeforePromptingAgent()
    {
        Assert.Equal(string.Empty, TaskExecutor.GetAllowedVerificationCommand("Remove-Item -Recurse C:\\"));
        Assert.Equal(string.Empty, TaskExecutor.GetAllowedVerificationCommand("npm run build; echo still-running"));
    }

    [Fact]
    public void TaskExecutor_PreservesAllowedVerificationCommandBeforePromptingAgent()
    {
        Assert.Equal("dotnet test", TaskExecutor.GetAllowedVerificationCommand(" dotnet test "));
        Assert.Equal("cmd /c cd /d \"front end\" && npm run build", TaskExecutor.GetAllowedVerificationCommand("cmd /c cd /d \"front end\" && npm run build"));
    }

    private sealed class TestLlmProvider(string content) : ILlmProvider
    {
        public string Name => "test-llm-provider";
        public string DefaultModel => "test-model";

        public Task<ChatResponse> GenerateResponseAsync(ChatContext context, IEnumerable<ToolDefinition> tools, CancellationToken ct = default)
        {
            return Task.FromResult(new ChatResponse
            {
                Model = context.Model,
                Content = new List<ChatContent> { ChatContent.CreateText(content) }
            });
        }

        public async IAsyncEnumerable<StreamChunk> GenerateStreamAsync(ChatContext context, IEnumerable<ToolDefinition> tools, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}
