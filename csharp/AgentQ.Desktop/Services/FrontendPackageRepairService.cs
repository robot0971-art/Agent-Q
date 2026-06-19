using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentQ.Api;

namespace AgentQ.Desktop.Services;

public sealed record FrontendPackageRepairResult(
    bool Succeeded,
    bool Changed,
    string PackageJsonPath,
    IReadOnlyList<string> PatchedFields,
    IReadOnlyList<string> SuggestedCommands,
    IReadOnlyList<string> Warnings,
    string Summary);

public sealed class FrontendPackageRepairService
{
    private static readonly JsonSerializerOptions JsonOptions = AgentQJsonOptions.Indented;

    public async Task<FrontendPackageRepairResult> RepairViteReactPackageAsync(
        string workspaceRoot,
        string failureKind,
        CancellationToken ct = default)
    {
        var packageJsonPath = Path.Combine(workspaceRoot, "package.json");
        if (!WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceRoot, packageJsonPath))
        {
            return Failed(packageJsonPath, "package.json is outside the selected workspace after path resolution.");
        }

        if (!File.Exists(packageJsonPath))
        {
            return Failed(packageJsonPath, "No package.json was found at the workspace root.");
        }

        JsonObject package;
        try
        {
            var text = await File.ReadAllTextAsync(packageJsonPath, ct);
            package = JsonNode.Parse(text) as JsonObject
                ?? throw new JsonException("package.json root must be an object.");
        }
        catch (JsonException ex)
        {
            return Failed(packageJsonPath, $"package.json could not be parsed: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Failed(packageJsonPath, $"package.json could not be read: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failed(packageJsonPath, $"package.json could not be read: {ex.Message}");
        }

        var warnings = new List<string>();
        if (!IsLikelyViteReactWorkspace(workspaceRoot, package))
        {
            warnings.Add("Workspace did not have enough Vite/React evidence for deterministic package repair.");
            return new FrontendPackageRepairResult(
                Succeeded: false,
                Changed: false,
                PackageJsonPath: packageJsonPath,
                PatchedFields: [],
                SuggestedCommands: [],
                Warnings: warnings,
                Summary: "Skipped package repair because the framework was not confidently identified as Vite/React.");
        }

        var patchedFields = new List<string>();
        var repairScripts = ShouldRepairScripts(failureKind);
        var scripts = (JsonObject?)null;
        if (repairScripts &&
            !TryGetOrCreateObject(package, "scripts", warnings, out scripts))
        {
            return CannotPatch(packageJsonPath, warnings);
        }

        if (repairScripts &&
            !HasStringProperty(scripts!, "dev"))
        {
            scripts!["dev"] = "vite --host 127.0.0.1";
            patchedFields.Add("scripts.dev");
        }

        if (repairScripts &&
            !HasStringProperty(scripts!, "build"))
        {
            scripts!["build"] = "vite build";
            patchedFields.Add("scripts.build");
        }

        if (ShouldRepairDependencies(failureKind))
        {
            if (!TryGetOrCreateObject(package, "dependencies", warnings, out var dependencies) ||
                !TryGetOrCreateObject(package, "devDependencies", warnings, out var devDependencies))
            {
                return CannotPatch(packageJsonPath, warnings);
            }

            AddMissingString(dependencies!, "react", "latest", patchedFields, "dependencies.react");
            AddMissingString(dependencies!, "react-dom", "latest", patchedFields, "dependencies.react-dom");

            AddMissingString(devDependencies!, "vite", "latest", patchedFields, "devDependencies.vite");
            AddMissingString(devDependencies!, "@vitejs/plugin-react", "latest", patchedFields, "devDependencies.@vitejs/plugin-react");
        }

        if (patchedFields.Count == 0)
        {
            return new FrontendPackageRepairResult(
                Succeeded: true,
                Changed: false,
                PackageJsonPath: packageJsonPath,
                PatchedFields: [],
                SuggestedCommands: BuildSuggestedCommands(failureKind),
                Warnings: warnings,
                Summary: "package.json already contains the expected Vite/React scripts and dependencies.");
        }

        await File.WriteAllTextAsync(packageJsonPath, package.ToJsonString(JsonOptions) + Environment.NewLine, ct);
        return new FrontendPackageRepairResult(
            Succeeded: true,
            Changed: true,
            PackageJsonPath: packageJsonPath,
            PatchedFields: patchedFields,
            SuggestedCommands: BuildSuggestedCommands(failureKind),
            Warnings: warnings,
            Summary: $"Patched package.json fields: {string.Join(", ", patchedFields)}.");
    }

    public static ToolReplayEntry CreateReplayEntry(FrontendPackageRepairResult result)
    {
        var now = DateTime.UtcNow;
        return new ToolReplayEntry
        {
            StartedAt = now,
            CompletedAt = now,
            DurationMs = 0,
            ToolName = "frontend_package_repair",
            ToolUseId = $"frontend-package-repair-{now:yyyyMMddHHmmssfff}",
            InputJson = JsonSerializer.Serialize(new
            {
                packageJsonPath = result.PackageJsonPath
            }),
            ResultPreview = JsonSerializer.Serialize(new
            {
                succeeded = result.Succeeded,
                changed = result.Changed,
                patchedFields = result.PatchedFields,
                suggestedCommands = result.SuggestedCommands,
                warnings = result.Warnings,
                summary = result.Summary
            }),
            IsError = !result.Succeeded
        };
    }

    private static FrontendPackageRepairResult Failed(string packageJsonPath, string summary) =>
        new(
            Succeeded: false,
            Changed: false,
            PackageJsonPath: packageJsonPath,
            PatchedFields: [],
            SuggestedCommands: [],
            Warnings: [summary],
            Summary: summary);

    private static FrontendPackageRepairResult CannotPatch(string packageJsonPath, IReadOnlyList<string> warnings) =>
        new(
            Succeeded: false,
            Changed: false,
            PackageJsonPath: packageJsonPath,
            PatchedFields: [],
            SuggestedCommands: [],
            Warnings: warnings,
            Summary: "Skipped package repair because package.json contains an existing non-object manifest field.");

    private static bool ShouldRepairScripts(string failureKind) =>
        failureKind.Equals("missing-npm-script", StringComparison.OrdinalIgnoreCase) ||
        failureKind.Equals("missing-dependency", StringComparison.OrdinalIgnoreCase) ||
        failureKind.Equals("dev-server-failure", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldRepairDependencies(string failureKind) =>
        failureKind.Equals("missing-dependency", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> BuildSuggestedCommands(string failureKind)
    {
        if (ShouldRepairDependencies(failureKind))
        {
            return ["npm install", "npm run build", "npm run dev"];
        }

        return ["npm run build", "npm run dev"];
    }

    private static bool TryGetOrCreateObject(
        JsonObject parent,
        string propertyName,
        ICollection<string> warnings,
        out JsonObject? result)
    {
        if (parent[propertyName] is JsonObject existing)
        {
            result = existing;
            return true;
        }

        if (parent.ContainsKey(propertyName))
        {
            warnings.Add($"{propertyName} already exists but is not an object.");
            result = null;
            return false;
        }

        var created = new JsonObject();
        parent[propertyName] = created;
        result = created;
        return true;
    }

    private static bool HasStringProperty(JsonObject parent, string propertyName) =>
        parent.TryGetPropertyValue(propertyName, out var value) &&
        value is JsonValue jsonValue &&
        jsonValue.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text);

    private static void AddMissingString(
        JsonObject parent,
        string propertyName,
        string value,
        ICollection<string> patchedFields,
        string fieldName)
    {
        if (parent.ContainsKey(propertyName))
        {
            return;
        }

        parent[propertyName] = value;
        patchedFields.Add(fieldName);
    }

    private static bool IsLikelyViteReactWorkspace(string workspaceRoot, JsonObject package)
    {
        var packageEvidence =
            ObjectContains(package, "dependencies", "react") ||
            ObjectContains(package, "dependencies", "react-dom") ||
            ObjectContains(package, "devDependencies", "vite") ||
            ObjectContains(package, "devDependencies", "@vitejs/plugin-react") ||
            ObjectContains(package, "scripts", "dev", "vite") ||
            ObjectContains(package, "scripts", "build", "vite");

        var fileEvidence =
            File.Exists(Path.Combine(workspaceRoot, "vite.config.js")) ||
            File.Exists(Path.Combine(workspaceRoot, "vite.config.ts")) ||
            File.Exists(Path.Combine(workspaceRoot, "src", "main.jsx")) ||
            File.Exists(Path.Combine(workspaceRoot, "src", "main.tsx")) ||
            File.Exists(Path.Combine(workspaceRoot, "src", "App.jsx")) ||
            File.Exists(Path.Combine(workspaceRoot, "src", "App.tsx"));

        var indexEvidence = FileContains(Path.Combine(workspaceRoot, "index.html"), "/src/main.");
        return packageEvidence || (fileEvidence && indexEvidence);
    }

    private static bool ObjectContains(JsonObject parent, string objectName, string propertyName, string? valueContains = null)
    {
        if (parent[objectName] is not JsonObject child ||
            !child.TryGetPropertyValue(propertyName, out var value) ||
            value is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var text))
        {
            return false;
        }

        return valueContains == null ||
               text.Contains(valueContains, StringComparison.OrdinalIgnoreCase);
    }

    private static bool FileContains(string path, string needle)
    {
        try
        {
            return File.Exists(path) &&
                   File.ReadAllText(path).Contains(needle, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
