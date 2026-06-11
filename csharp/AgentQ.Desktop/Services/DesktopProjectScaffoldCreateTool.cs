using System.IO;
using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopProjectScaffoldCreateTool(
    string workspaceRoot,
    WorkerScaffoldExecutor? executor = null,
    ProjectScaffoldPlanRegistry? planRegistry = null) : ITool
{
    private readonly WorkerScaffoldExecutor _executor = executor ?? new WorkerScaffoldExecutor();
    private readonly ProjectScaffoldPlanRegistry _planRegistry = planRegistry ?? new ProjectScaffoldPlanRegistry();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string Name => "create_project_scaffold";

    public string Description =>
        "Create files for an approved deterministic greenfield project scaffold plan. Requires planId and planHash from plan_project_scaffold, writes only registry-approved files, and does not overwrite existing files unless explicitly allowed.";

    public bool RequiresPermission => true;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            intent = new
            {
                type = "object",
                description = "Optional display snapshot of the approved project scaffold intent returned by plan_project_scaffold.",
                properties = new
                {
                    projectType = new { type = "string" },
                    language = new { type = "string" },
                    framework = new { type = "string" },
                    style = new { type = "string" }
                }
            },
            plan = new
            {
                type = "object",
                description = "Optional display snapshot of the approved project scaffold plan returned by plan_project_scaffold.",
                properties = new
                {
                    name = new { type = "string" },
                    files = new { type = "array", items = new { type = "string" } },
                    verificationCommands = new { type = "array", items = new { type = "string" } }
                }
            },
            planId = new { type = "string", description = "The approved plan id returned by plan_project_scaffold or preflight." },
            planHash = new { type = "string", description = "The approved SHA-256 plan hash returned by plan_project_scaffold or preflight." },
            overwriteExistingFiles = new { type = "boolean", description = "Whether existing files may be overwritten. Defaults to false." }
        },
        required = new[] { "planId", "planHash" }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!TryGetString(input, "planId", out var planId) ||
            !_planRegistry.TryGet(planId, out var record))
        {
            return ToolResult.Error("Project scaffold planId is missing or unknown. Call plan_project_scaffold first, then pass its planId and planHash unchanged.");
        }

        if (!ProjectScaffoldPlanRegistry.MatchesWorkspace(record.WorkspaceRoot, workspaceRoot))
        {
            return ToolResult.Error("Project scaffold planId belongs to a different workspace. Call plan_project_scaffold again for the selected workspace.");
        }

        if (!TryGetString(input, "planHash", out var planHash) ||
            !string.Equals(record.PlanHash, planHash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ToolResult.Error("Project scaffold plan hash is missing or does not match the approved planId. Call plan_project_scaffold first, then pass its planId and planHash unchanged.");
        }

        if (!InputSnapshotMatchesRegistry(input, record, out var mismatch))
        {
            return ToolResult.Error(mismatch);
        }

        var intent = record.Intent;
        var plan = record.Plan;
        var overwrite = TryGetBool(input, "overwriteExistingFiles", fallback: false);
        var validationIssues = ValidatePlanFiles(plan, workspaceRoot);
        if (validationIssues.Count > 0)
        {
            return ToolResult.Error("Project scaffold plan is not safe to execute: " + string.Join("; ", validationIssues));
        }

        var workerPlan = ToWorkerPlan(plan.Name, intent, plan);
        var result = await _executor.ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                Plan = workerPlan,
                WorkspaceRoot = workspaceRoot,
                FeatureName = intent.ProjectType,
                OverwriteExistingFiles = overwrite,
                EnableAutoWiring = false
            },
            ct);

        return ToolResult.Success(JsonSerializer.Serialize(new
        {
            succeeded = result.Succeeded,
            intent = new
            {
                projectType = intent.ProjectType,
                language = intent.Language,
                framework = intent.Framework,
                style = intent.Style
            },
            plan = new
            {
                name = plan.Name,
                files = plan.Files,
                verificationCommands = plan.VerificationCommands
            },
            planId = record.PlanId,
            planHash,
            createdFiles = result.CreatedFiles,
            skippedFiles = result.SkippedFiles,
            issues = result.Issues,
            verificationCommands = result.VerificationCommands,
            overwriteExistingFiles = overwrite
        }));
    }

    private static bool InputSnapshotMatchesRegistry(
        Dictionary<string, object?> input,
        ProjectScaffoldPlanRecord record,
        out string mismatch)
    {
        if (TryGetObject<ProjectScaffoldIntentModel>(input, "intent", out var intent) &&
            !ProjectScaffoldPlanner.VerifyPlanHash(intent, record.Plan, record.PlanHash))
        {
            mismatch = "Project scaffold intent snapshot does not match the approved planId.";
            return false;
        }

        if (TryGetObject<ProjectScaffoldPlanModel>(input, "plan", out var plan) &&
            !ProjectScaffoldPlanner.VerifyPlanHash(record.Intent, plan, record.PlanHash))
        {
            mismatch = "Project scaffold plan snapshot does not match the approved planId.";
            return false;
        }

        mismatch = string.Empty;
        return true;
    }

    private static List<string> ValidatePlanFiles(ProjectScaffoldPlanModel plan, string workspaceRoot)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(plan.Name))
        {
            issues.Add("plan.name is required");
        }

        if (plan.Files.Count == 0)
        {
            issues.Add("plan.files must include at least one file");
            return issues;
        }

        var root = Path.GetFullPath(workspaceRoot);
        foreach (var file in plan.Files)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                issues.Add("plan.files contains an empty path");
                continue;
            }

            if (Path.IsPathRooted(file))
            {
                issues.Add($"plan file path must be relative: {file}");
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(root, file));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                issues.Add($"plan file path is invalid: {file}");
                continue;
            }

            if (!WorkspacePathResolver.IsInsideWorkspace(root, fullPath))
            {
                issues.Add($"plan file path escapes the workspace: {file}");
                continue;
            }

            if (!WorkspacePathResolver.IsResolvedInsideWorkspace(root, fullPath))
            {
                issues.Add($"plan file path resolves outside the workspace: {file}");
            }
        }

        return issues;
    }

    private static WorkerPlan ToWorkerPlan(
        string goal,
        ProjectScaffoldIntentModel intent,
        ProjectScaffoldPlanModel scaffoldPlan)
    {
        var plan = new WorkerPlan
        {
            Goal = goal,
            Language = intent.Language,
            Framework = intent.Framework,
            Summary = scaffoldPlan.Name,
            VerificationCommands = scaffoldPlan.VerificationCommands.ToList()
        };

        foreach (var file in scaffoldPlan.Files)
        {
            plan.Steps.Add(new WorkerPlanStep
            {
                Kind = WorkerPlanStepKind.CreateFile,
                Path = file,
                Reason = $"Create {scaffoldPlan.Name} file.",
                ExpectedChange = $"Add {file}.",
                RequiresApproval = false
            });
        }

        foreach (var command in scaffoldPlan.VerificationCommands)
        {
            plan.Steps.Add(new WorkerPlanStep
            {
                Kind = WorkerPlanStepKind.Verify,
                Reason = "Run scaffold verification.",
                ExpectedChange = command
            });
        }

        return plan;
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

    private static bool TryGetObject<T>(Dictionary<string, object?> input, string key, out T value)
        where T : class
    {
        if (input.TryGetValue(key, out var raw) && raw != null)
        {
            if (raw is T typed)
            {
                value = typed;
                return true;
            }

            try
            {
                if (raw is JsonElement element)
                {
                    var elementValue = element.Deserialize<T>(JsonOptions);
                    if (elementValue != null)
                    {
                        value = elementValue;
                        return true;
                    }
                }
                else
                {
                    var json = JsonSerializer.Serialize(raw, JsonOptions);
                    var deserialized = JsonSerializer.Deserialize<T>(json, JsonOptions);
                    if (deserialized != null)
                    {
                        value = deserialized;
                        return true;
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to the missing-parameter error.
            }
        }

        value = null!;
        return false;
    }

    private static bool TryGetBool(Dictionary<string, object?> input, string key, bool fallback)
    {
        if (!input.TryGetValue(key, out var raw) || raw == null)
        {
            return fallback;
        }

        return raw switch
        {
            bool boolean => boolean,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }
}
