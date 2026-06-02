using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopProjectScaffoldPlanTool(
    string workspaceRoot,
    ProjectScaffoldPlanner? planner = null,
    ProjectScaffoldPlanRegistry? planRegistry = null) : ITool
{
    private readonly ProjectScaffoldPlanner _planner = planner ?? new ProjectScaffoldPlanner();
    private readonly ProjectScaffoldPlanRegistry _planRegistry = planRegistry ?? new ProjectScaffoldPlanRegistry();

    public string Name => "plan_project_scaffold";

    public string Description =>
        "Plan a greenfield project scaffold without creating files. Use this when the user changes project type, language, framework, or style after an initial project request.";

    public bool RequiresPermission => false;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            request = new { type = "string", description = "The user's latest project creation request or clarification." },
            workspaceRoot = new { type = "string", description = "Optional workspace root override. Defaults to the selected workspace." }
        },
        required = new[] { "request" }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!TryGetString(input, "request", out var request))
        {
            return Task.FromResult(ToolResult.Error("Missing required parameter: request"));
        }

        var root = TryGetString(input, "workspaceRoot", out var overrideRoot)
            ? overrideRoot
            : workspaceRoot;

        try
        {
            var result = _planRegistry.Register(_planner.Plan(request, root), root);
            return Task.FromResult(ToolResult.Success(JsonSerializer.Serialize(new
            {
                isGreenfieldRequest = result.IsGreenfieldRequest,
                canProceed = result.CanProceed,
                clarifyingQuestion = string.IsNullOrWhiteSpace(result.ClarifyingQuestion) ? null : result.ClarifyingQuestion,
                intent = result.Intent == null
                    ? null
                    : new
                    {
                        projectType = result.Intent.ProjectType,
                        language = result.Intent.Language,
                        framework = result.Intent.Framework,
                        style = result.Intent.Style
                    },
                plan = result.Plan == null
                    ? null
                    : new
                    {
                        name = result.Plan.Name,
                        files = result.Plan.Files,
                        verificationCommands = result.Plan.VerificationCommands
                    },
                planId = string.IsNullOrWhiteSpace(result.PlanId) ? null : result.PlanId,
                planHash = string.IsNullOrWhiteSpace(result.PlanHash) ? null : result.PlanHash,
                reasons = result.Reasons,
                planContext = ProjectScaffoldPlanner.BuildPlanContext(result)
            })));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to plan project scaffold: {ex.Message}"));
        }
    }

    private static bool TryGetString(Dictionary<string, object?> input, string key, out string value)
    {
        if (input.TryGetValue(key, out var raw))
        {
            if (raw is string text && !string.IsNullOrWhiteSpace(text))
            {
                value = text.Trim();
                return true;
            }

            if (raw is JsonElement { ValueKind: JsonValueKind.String } element)
            {
                var jsonText = element.GetString();
                if (!string.IsNullOrWhiteSpace(jsonText))
                {
                    value = jsonText.Trim();
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }
}
