using System.IO;
using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopProjectScaffoldCreateTool(
    string workspaceRoot,
    WorkerScaffoldExecutor? executor = null) : ITool
{
    private readonly WorkerScaffoldExecutor _executor = executor ?? new WorkerScaffoldExecutor();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string Name => "create_project_scaffold";

    public string Description =>
        "Create files for an approved deterministic greenfield project scaffold plan. Requires intent and plan from plan_project_scaffold, writes only plan-approved files, and does not overwrite existing files unless explicitly allowed.";

    public bool RequiresPermission => true;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            intent = new
            {
                type = "object",
                description = "The approved project scaffold intent returned by plan_project_scaffold.",
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
                description = "The approved project scaffold plan returned by plan_project_scaffold.",
                properties = new
                {
                    name = new { type = "string" },
                    files = new { type = "array", items = new { type = "string" } },
                    verificationCommands = new { type = "array", items = new { type = "string" } }
                }
            },
            request = new { type = "string", description = "Legacy fallback only. If intent or plan is missing, call plan_project_scaffold first instead of creating files from request alone." },
            planHash = new { type = "string", description = "The approved SHA-256 plan hash returned by plan_project_scaffold or preflight." },
            overwriteExistingFiles = new { type = "boolean", description = "Whether existing files may be overwritten. Defaults to false." }
        },
        required = new[] { "intent", "plan", "planHash" }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!TryGetObject<ProjectScaffoldIntentModel>(input, "intent", out var intent) ||
            !TryGetObject<ProjectScaffoldPlanModel>(input, "plan", out var plan))
        {
            return ToolResult.Error("Missing required parameters: intent and plan. Call plan_project_scaffold first, then pass its intent and plan to create_project_scaffold.");
        }

        if (!TryGetString(input, "planHash", out var planHash) ||
            !ProjectScaffoldPlanner.VerifyPlanHash(intent, plan, planHash))
        {
            return ToolResult.Error("Project scaffold plan hash is missing or does not match the approved intent and plan. Call plan_project_scaffold first, then pass its intent, plan, and planHash unchanged.");
        }

        var overwrite = TryGetBool(input, "overwriteExistingFiles", fallback: false);
        var validationIssues = ValidatePlanFiles(plan, workspaceRoot);
        if (validationIssues.Count > 0)
        {
            return ToolResult.Error("Project scaffold plan is not safe to execute: " + string.Join("; ", validationIssues));
        }

        var existingFiles = ExistingPlanFiles(plan, workspaceRoot);
        if (existingFiles.Count > 0 && !overwrite)
        {
            return ToolResult.Success(JsonSerializer.Serialize(new
            {
                succeeded = false,
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
                planHash,
                createdFiles = Array.Empty<string>(),
                skippedFiles = existingFiles,
                issues = new[] { "Project scaffold was not created because target files already exist. Re-run with overwriteExistingFiles=true only if overwriting is intended." },
                verificationCommands = plan.VerificationCommands,
                overwriteExistingFiles = overwrite
            }));
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
            planHash,
            createdFiles = result.CreatedFiles,
            skippedFiles = result.SkippedFiles,
            issues = result.Issues,
            verificationCommands = result.VerificationCommands,
            overwriteExistingFiles = overwrite
        }));
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
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
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

            var fullPath = Path.GetFullPath(Path.Combine(root, file));
            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"plan file path escapes the workspace: {file}");
            }
        }

        return issues;
    }

    private static List<string> ExistingPlanFiles(ProjectScaffoldPlanModel plan, string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var existing = new List<string>();
        foreach (var file in plan.Files)
        {
            if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(root, file));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                existing.Add(file.Replace('\\', '/'));
            }
        }

        return existing;
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
