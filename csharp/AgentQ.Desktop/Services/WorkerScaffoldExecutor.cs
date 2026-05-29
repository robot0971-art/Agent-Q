using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public sealed class WorkerScaffoldExecutor
{
    private readonly WorkerScaffoldContextBuilder _contextBuilder = new();
    private readonly WorkerScaffoldAutoWirer _autoWirer = new();

    public async Task<WorkerScaffoldExecutionResult> ExecuteAsync(
        WorkerScaffoldExecutionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var root = Path.GetFullPath(request.WorkspaceRoot);
        if (!Directory.Exists(root))
        {
            return new WorkerScaffoldExecutionResult
            {
                Succeeded = false,
                Issues = [$"Workspace root does not exist: {request.WorkspaceRoot}"]
            };
        }

        var feature = WorkerScaffoldName.From(request.FeatureName);
        var scaffoldContext = request.ScaffoldContext ?? _contextBuilder.Build(root, request.Plan);
        var result = new WorkerScaffoldExecutionResult
        {
            VerificationCommands = request.Plan.VerificationCommands.ToList()
        };

        foreach (var step in request.Plan.Steps.Where(step => step.Kind == WorkerPlanStepKind.CreateFile))
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = ResolveTemplatePath(step.Path, feature, scaffoldContext);
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                result.Issues.Add($"Scaffold path is not workspace-relative: {step.Path}");
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!IsInside(root, fullPath))
            {
                result.Issues.Add($"Scaffold path escapes workspace: {step.Path}");
                continue;
            }

            if (File.Exists(fullPath) && !request.OverwriteExistingFiles)
            {
                result.SkippedFiles.Add(relativePath.Replace('\\', '/'));
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(
                fullPath,
                WorkerScaffoldTemplateRenderer.Render(request.Plan, relativePath, feature, scaffoldContext),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                ct);
            result.CreatedFiles.Add(relativePath.Replace('\\', '/'));
        }

        if (request.EnableAutoWiring)
        {
            result.WiringChanges.AddRange(await _autoWirer.WireAsync(
                root,
                request.Plan,
                feature,
                scaffoldContext,
                result.CreatedFiles,
                result.Issues,
                ct));
            result.WiredFiles.AddRange(result.WiringChanges.Select(change => change.Path));
        }

        result.Succeeded = result.Issues.Count == 0;
        return result;
    }

    private static string ResolveTemplatePath(
        string path,
        WorkerScaffoldName feature,
        WorkerScaffoldContext context)
    {
        return path.Replace('\\', '/')
            .Replace("<Feature>", feature.Pascal, StringComparison.Ordinal)
            .Replace("<feature>", feature.Kebab, StringComparison.Ordinal)
            .Replace("<feature_snake>", feature.Snake, StringComparison.Ordinal)
            .Replace("<feature_dir>", $"{context.FeatureRoot}/{feature.Kebab}", StringComparison.Ordinal)
            .Replace("<source_root>", context.SourceRoot, StringComparison.Ordinal)
            .Replace("<test_root>", context.TestRoot, StringComparison.Ordinal)
            .Replace("<python_app>", context.PythonAppRoot, StringComparison.Ordinal)
            .Replace("<python_router>", context.PythonRouterRoot, StringComparison.Ordinal)
            .Replace("<ts_test_suffix>", context.TypeScriptTestSuffix, StringComparison.Ordinal)
            .Replace("<task>", feature.Kebab, StringComparison.Ordinal)
            .Replace("<Module>", feature.Pascal, StringComparison.Ordinal)
            .Replace("<app>", "app", StringComparison.Ordinal)
            .Replace("<package>", "app", StringComparison.Ordinal)
            .Replace("<timestamp>", DateTime.UtcNow.ToString("yyyyMMddHHmmss"), StringComparison.Ordinal)
            .Trim('/');
    }

    private static bool IsInside(string root, string fullPath)
    {
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record WorkerScaffoldName(string Pascal, string Camel, string Kebab, string Snake)
{
    public static WorkerScaffoldName From(string value)
    {
        var words = Regex.Matches(value, "[A-Za-z0-9]+")
            .Select(match => match.Value)
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToList();
        if (words.Count == 0)
        {
            words.Add("Feature");
        }

        var pascal = string.Concat(words.Select(ToTitle));
        var camel = char.ToLowerInvariant(pascal[0]) + pascal[1..];
        var kebab = string.Join("-", words.Select(word => word.ToLowerInvariant()));
        var snake = string.Join("_", words.Select(word => word.ToLowerInvariant()));
        return new WorkerScaffoldName(pascal, camel, kebab, snake);
    }

    private static string ToTitle(string value)
    {
        var lower = value.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }
}
