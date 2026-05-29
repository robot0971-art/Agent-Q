using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public enum TaskStepKind
{
    Investigate,
    Implement,
    Test,
    Verify
}

public sealed class TaskStep
{
    public int Order { get; set; }
    public string Description { get; set; } = string.Empty;
    
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskStepKind Kind { get; set; }
    public List<string> RelevantFiles { get; set; } = [];
    public List<string> DependsOn { get; set; } = [];
    public string VerificationCommand { get; set; } = string.Empty;
}

public sealed class TaskPlan
{
    public string Goal { get; set; } = string.Empty;
    public List<TaskStep> Steps { get; set; } = [];
    public List<string> Risks { get; set; } = [];
}

public sealed class TaskDecomposer
{
    public async Task<TaskPlan> DecomposeAsync(
        string userGoal,
        WorkspaceAnalysis workspaceAnalysis,
        ILlmProvider provider,
        ProviderConfiguration config,
        CancellationToken ct)
    {
        var projectMapInfo = string.Join("\n", workspaceAnalysis.ProjectMap.Take(100)); // Limit size
        var keyFilesInfo = string.Join(", ", workspaceAnalysis.KeyFiles);
        var keySymbolsInfo = string.Join(", ", workspaceAnalysis.KeySymbols);

        var prompt = $$"""
You are a software architect planning agent. Your task is to break down a high-level goal into a sequence of small, independent, execution-ready steps (tasks).

User Goal:
"{{userGoal}}"

Project Context:
- Project Type: {{workspaceAnalysis.ProjectType}}
- Framework: {{workspaceAnalysis.Framework}}
- Key Files: {{keyFilesInfo}}
- Key Symbols: {{keySymbolsInfo}}
- Project Map Snapshot:
{{projectMapInfo}}

Please output a JSON plan with the following structure:
{
  "Goal": "Summary of the main goal",
  "Steps": [
    {
      "Order": 1,
      "Description": "Step-by-step description of what to do",
      "Kind": "Investigate" or "Implement" or "Test" or "Verify",
      "RelevantFiles": ["file1.cs", "file2.cs"],
      "DependsOn": ["1"], // Order of previous steps this depends on
      "VerificationCommand": "command to run to verify this step (e.g. dotnet test --filter MyTest)"
    }
  ],
  "Risks": [
    "Potential risks, regression vectors, or things to watch out for"
  ]
}

Provide ONLY the raw JSON. Do not write markdown wrapping, conversational text, or any explanations outside of the JSON.
""";

        var chatContext = new ChatContext
        {
            Model = config.Model,
            Stream = false,
            MaxTokens = 2000,
            Messages = new List<ChatMessage> { ChatMessage.UserText(prompt) }
        };

        var response = await provider.GenerateResponseAsync(chatContext, Enumerable.Empty<ToolDefinition>(), ct);
        var jsonText = string.Join("\n", response.Content.Select(c => c.Text)).Trim();

        // Strip markdown backticks if any
        if (jsonText.StartsWith("```"))
        {
            var lines = jsonText.Split('\n');
            var strippedLines = lines.Skip(1).Take(lines.Length - 2);
            if (lines.First().StartsWith("```json"))
            {
                jsonText = string.Join("\n", strippedLines).Trim();
            }
            else
            {
                jsonText = string.Join("\n", strippedLines).Trim();
            }
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };
            var plan = JsonSerializer.Deserialize<TaskPlan>(jsonText, options);
            if (plan != null)
            {
                return plan;
            }
        }
        catch
        {
            // Fallback plan if JSON deserialization fails
        }

        // Return a basic fallback plan
        return new TaskPlan
        {
            Goal = userGoal,
            Steps = new List<TaskStep>
            {
                new TaskStep
                {
                    Order = 1,
                    Description = $"Perform the task: {userGoal}",
                    Kind = TaskStepKind.Implement,
                    RelevantFiles = workspaceAnalysis.KeyFiles.ToList()
                }
            },
            Risks = new List<string> { "Fallback plan generated due to planning failure." }
        };
    }
}
