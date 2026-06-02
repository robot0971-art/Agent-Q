using System.IO;
using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopProjectScaffoldCreateTool(
    string workspaceRoot,
    ProjectScaffoldPlanner? planner = null,
    WorkerScaffoldExecutor? executor = null) : ITool
{
    private readonly ProjectScaffoldPlanner _planner = planner ?? new ProjectScaffoldPlanner();
    private readonly WorkerScaffoldExecutor _executor = executor ?? new WorkerScaffoldExecutor();

    public string Name => "create_project_scaffold";

    public string Description =>
        "Create files for a deterministic greenfield project scaffold plan. Re-plans from the latest request, writes only planner-approved files, and does not overwrite existing files unless explicitly allowed.";

    public bool RequiresPermission => true;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            request = new { type = "string", description = "The user's latest concrete project creation request." },
            overwriteExistingFiles = new { type = "boolean", description = "Whether existing files may be overwritten. Defaults to false." }
        },
        required = new[] { "request" }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        if (!TryGetString(input, "request", out var request))
        {
            return ToolResult.Error("Missing required parameter: request");
        }

        var overwrite = TryGetBool(input, "overwriteExistingFiles", fallback: false);
        var planning = _planner.Plan(request, workspaceRoot);
        if (!planning.IsGreenfieldRequest)
        {
            return ToolResult.Error("Request is not a greenfield project scaffold request.");
        }

        if (!planning.CanProceed || planning.Intent == null || planning.Plan == null)
        {
            return ToolResult.Error(string.IsNullOrWhiteSpace(planning.ClarifyingQuestion)
                ? "Project scaffold request needs clarification before files can be created."
                : planning.ClarifyingQuestion);
        }

        var existingFiles = ExistingPlanFiles(planning.Plan, workspaceRoot);
        if (existingFiles.Count > 0 && !overwrite)
        {
            return ToolResult.Success(JsonSerializer.Serialize(new
            {
                succeeded = false,
                intent = new
                {
                    projectType = planning.Intent.ProjectType,
                    language = planning.Intent.Language,
                    framework = planning.Intent.Framework,
                    style = planning.Intent.Style
                },
                plan = new
                {
                    name = planning.Plan.Name,
                    files = planning.Plan.Files,
                    verificationCommands = planning.Plan.VerificationCommands
                },
                createdFiles = Array.Empty<string>(),
                skippedFiles = existingFiles,
                issues = new[] { "Project scaffold was not created because target files already exist. Re-run with overwriteExistingFiles=true only if overwriting is intended." },
                verificationCommands = planning.Plan.VerificationCommands,
                overwriteExistingFiles = overwrite
            }));
        }

        var workerPlan = ToWorkerPlan(request, planning.Intent, planning.Plan);
        var result = await _executor.ExecuteAsync(
            new WorkerScaffoldExecutionRequest
            {
                Plan = workerPlan,
                WorkspaceRoot = workspaceRoot,
                FeatureName = planning.Intent.ProjectType,
                OverwriteExistingFiles = overwrite,
                EnableAutoWiring = false
            },
            ct);

        return ToolResult.Success(JsonSerializer.Serialize(new
        {
            succeeded = result.Succeeded,
            intent = new
            {
                projectType = planning.Intent.ProjectType,
                language = planning.Intent.Language,
                framework = planning.Intent.Framework,
                style = planning.Intent.Style
            },
            plan = new
            {
                name = planning.Plan.Name,
                files = planning.Plan.Files,
                verificationCommands = planning.Plan.VerificationCommands
            },
            createdFiles = result.CreatedFiles,
            skippedFiles = result.SkippedFiles,
            issues = result.Issues,
            verificationCommands = result.VerificationCommands,
            overwriteExistingFiles = overwrite
        }));
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
