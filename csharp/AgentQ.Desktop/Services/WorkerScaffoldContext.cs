using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class WorkerScaffoldContext
{
    public string SourceRoot { get; init; } = "src";

    public string TestRoot { get; init; } = "tests";

    public string FeatureRoot { get; init; } = "src/features";

    public string PythonAppRoot { get; init; } = "app";

    public string PythonRouterRoot { get; init; } = "app/routers";

    public string TypeScriptTestSuffix { get; init; } = ".test";

    public bool UsesVitest { get; init; }

    public bool UsesJest { get; init; }
}

public sealed class WorkerScaffoldContextBuilder
{
    public WorkerScaffoldContext Build(string workspaceRoot, WorkerPlan plan)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var sourceRoot = FirstExisting(root, ["src", "app", "source"]) ?? "src";
        var testRoot = FirstExisting(root, ["tests", "test", "__tests__", Path.Combine(sourceRoot, "__tests__")]) ?? "tests";
        var featureRoot = FirstExisting(root, [Path.Combine(sourceRoot, "features"), Path.Combine(sourceRoot, "components"), sourceRoot])
            ?? Path.Combine(sourceRoot, "features");
        var pythonAppRoot = FirstExisting(root, ["app", "src", plan.Framework.Contains("Django", StringComparison.OrdinalIgnoreCase) ? "project" : "app"]) ?? "app";
        var pythonRouterRoot = FirstExisting(root, [Path.Combine(pythonAppRoot, "routers"), Path.Combine(pythonAppRoot, "api")])
            ?? Path.Combine(pythonAppRoot, "routers");
        var usesVitest = FileContains(root, "package.json", "vitest");
        var usesJest = FileContains(root, "package.json", "jest");

        return new WorkerScaffoldContext
        {
            SourceRoot = Normalize(sourceRoot),
            TestRoot = Normalize(testRoot),
            FeatureRoot = Normalize(featureRoot),
            PythonAppRoot = Normalize(pythonAppRoot),
            PythonRouterRoot = Normalize(pythonRouterRoot),
            TypeScriptTestSuffix = usesJest && !usesVitest ? ".spec" : ".test",
            UsesVitest = usesVitest,
            UsesJest = usesJest
        };
    }

    private static string? FirstExisting(string root, IEnumerable<string> candidates)
    {
        return candidates
            .Select(Normalize)
            .FirstOrDefault(candidate =>
            {
                var path = Path.Combine(root, candidate);
                return Directory.Exists(path) &&
                       WorkspacePathResolver.IsResolvedInsideWorkspace(root, path);
            });
    }

    private static bool FileContains(string root, string relativePath, string value)
    {
        var path = Path.Combine(root, relativePath);
        return File.Exists(path) &&
               WorkspacePathResolver.IsResolvedInsideWorkspace(root, path) &&
               File.ReadAllText(path).Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }
}
