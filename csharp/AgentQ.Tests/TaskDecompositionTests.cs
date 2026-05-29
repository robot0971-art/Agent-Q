using System.Collections.Generic;
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
