using System.Text.Json;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopProjectScaffoldVerifyTool(
    string workspaceRoot,
    DesktopVerificationRunner? runner = null,
    VerificationFailureClassifier? failureClassifier = null,
    ProjectScaffoldPlanRegistry? planRegistry = null) : ITool
{
    private readonly DesktopVerificationRunner _runner = runner ?? new DesktopVerificationRunner([]);
    private readonly VerificationFailureClassifier _failureClassifier = failureClassifier ?? new VerificationFailureClassifier();
    private readonly ProjectScaffoldPlanRegistry _planRegistry = planRegistry ?? new ProjectScaffoldPlanRegistry();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string Name => "verify_project_scaffold";

    public string Description =>
        "Run an approved project scaffold verification command from the plan returned by plan_project_scaffold. Only commands listed in plan.verificationCommands may run.";

    public bool RequiresPermission => true;

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            planId = new { type = "string", description = "The approved plan id returned by plan_project_scaffold or preflight." },
            plan = new
            {
                type = "object",
                description = "Optional display snapshot of the approved project scaffold plan containing verificationCommands.",
                properties = new
                {
                    name = new { type = "string" },
                    files = new { type = "array", items = new { type = "string" } },
                    verificationCommands = new { type = "array", items = new { type = "string" } }
                }
            },
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
            planHash = new { type = "string", description = "The approved SHA-256 plan hash returned by plan_project_scaffold or preflight." },
            command = new
            {
                type = "string",
                description = "Optional command to run. Must exactly match one entry in plan.verificationCommands. Defaults to the first verification command."
            },
            timeoutSeconds = new
            {
                type = "integer",
                description = "Optional timeout in seconds. Defaults to 120 and is capped at 600."
            }
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

        var plan = record.Plan;
        var commands = plan.VerificationCommands
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Select(command => command.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (commands.Count == 0)
        {
            return ToolResult.Error("Project scaffold plan does not include verificationCommands.");
        }

        var command = TryGetString(input, "command", out var requestedCommand)
            ? requestedCommand
            : commands[0];
        if (!commands.Contains(command, StringComparer.Ordinal))
        {
            return ToolResult.Error("Verification command is not part of the approved project scaffold plan.");
        }

        if (!VerificationCommandPolicy.IsAllowed(command))
        {
            return ToolResult.Error("Verification command is not allowed by the verification command policy.");
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(TryGetInt(input, "timeoutSeconds", 120), 1, 600));
        var verificationPlan = new AgentVerificationPlan
        {
            Title = string.IsNullOrWhiteSpace(plan.Name) ? "Project scaffold verification" : $"Verify {plan.Name}",
            Reason = "Run approved scaffold verification command.",
            Command = command
        };

        try
        {
            var result = await _runner.RunAsync(verificationPlan, workspaceRoot, timeout, projectAllowedCommands: null, ct);
            var failureAnalysis = result.Succeeded
                ? null
                : _failureClassifier.Analyze(verificationPlan, result, workspaceRoot);
            return ToolResult.Success(JsonSerializer.Serialize(new
            {
                succeeded = result.Succeeded,
                command,
                exitCode = result.ExitCode,
                standardOutput = Truncate(result.StandardOutput),
                standardError = Truncate(result.StandardError),
                combinedOutput = Truncate(result.CombinedOutput),
                failureAnalysis = failureAnalysis == null ? null : ToFailureAnalysisDto(failureAnalysis),
                repairPrompt = failureAnalysis == null
                    ? null
                    : DesktopPromptBuilder.BuildVerificationFixPrompt(verificationPlan, result, failureAnalysis),
                repairPlan = failureAnalysis == null ? null : BuildRepairPlan(plan, command, failureAnalysis),
                artifacts = result.Artifacts.Select(artifact => new
                {
                    artifact.Kind,
                    artifact.Path
                }).ToList()
            }));
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            var failureAnalysis = _failureClassifier.AnalyzeException(ex);
            return ToolResult.Success(JsonSerializer.Serialize(new
            {
                succeeded = false,
                command,
                exitCode = (int?)null,
                standardOutput = string.Empty,
                standardError = string.Empty,
                combinedOutput = ex.Message,
                failureAnalysis = ToFailureAnalysisDto(failureAnalysis),
                repairPrompt = DesktopPromptBuilder.BuildVerificationFixPrompt(verificationPlan, null, failureAnalysis),
                repairPlan = BuildRepairPlan(plan, command, failureAnalysis),
                artifacts = Array.Empty<object>()
            }));
        }
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

    private static object ToFailureAnalysisDto(VerificationFailureAnalysis analysis) => new
    {
        kind = analysis.Kind.ToString(),
        title = analysis.Title,
        summary = analysis.Summary,
        suggestedNextStep = analysis.SuggestedNextStep,
        evidence = analysis.Evidence,
        errorLocations = analysis.ErrorLocations.Select(location => new
        {
            location.FilePath,
            location.Line,
            location.Column,
            location.ErrorCode,
            location.Message
        }).ToList()
    };

    private static object BuildRepairPlan(
        ProjectScaffoldPlanModel plan,
        string command,
        VerificationFailureAnalysis analysis) => new
    {
        goal = $"Repair failed project scaffold verification: {plan.Name}",
        summary = analysis.Summary,
        failureKind = analysis.Kind.ToString(),
        suggestedNextStep = analysis.SuggestedNextStep,
        evidence = analysis.Evidence,
        verificationCommands = plan.VerificationCommands,
        failedCommand = command,
        risks = new[] { "Keep repairs scoped to files created by the approved project scaffold plan." }
    };

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

    private static int TryGetInt(Dictionary<string, object?> input, string key, int fallback)
    {
        if (!input.TryGetValue(key, out var raw) || raw == null)
        {
            return fallback;
        }

        return raw switch
        {
            int value => value,
            long value => value is > int.MaxValue or < int.MinValue ? fallback : (int)value,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
    }

    private static string Truncate(string value)
    {
        const int limit = 8000;
        return value.Length <= limit ? value : value[..limit] + "\n... truncated ...";
    }
}
