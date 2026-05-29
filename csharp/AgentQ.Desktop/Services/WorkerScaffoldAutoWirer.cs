using System.IO;
using System.Text;

namespace AgentQ.Desktop.Services;

public sealed class WorkerScaffoldAutoWirer
{
    public async Task<IReadOnlyList<string>> WireAsync(
        string workspaceRoot,
        WorkerPlan plan,
        WorkerScaffoldName feature,
        WorkerScaffoldContext context,
        IReadOnlyList<string> createdFiles,
        List<string> issues,
        CancellationToken ct = default)
    {
        var wired = new List<string>();
        if (createdFiles.Count == 0)
        {
            return wired;
        }

        if (plan.Language.Contains("typescript", StringComparison.OrdinalIgnoreCase) ||
            plan.Framework.Contains("react", StringComparison.OrdinalIgnoreCase))
        {
            await WireReactIndexAsync(workspaceRoot, feature, createdFiles, wired, ct);
        }

        if (plan.Framework.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
        {
            await WireFastApiRouterAsync(workspaceRoot, feature, context, createdFiles, wired, issues, ct);
        }

        if (plan.Language.Contains("rust", StringComparison.OrdinalIgnoreCase))
        {
            await WireRustModuleAsync(workspaceRoot, feature, createdFiles, wired, issues, ct);
        }

        return wired;
    }

    private static async Task WireReactIndexAsync(
        string workspaceRoot,
        WorkerScaffoldName feature,
        IReadOnlyList<string> createdFiles,
        List<string> wired,
        CancellationToken ct)
    {
        var viewFile = createdFiles.FirstOrDefault(file =>
            file.EndsWith($"{feature.Pascal}View.tsx", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(viewFile))
        {
            return;
        }

        var directory = Path.GetDirectoryName(viewFile)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var indexRelative = $"{directory}/index.ts";
        var indexPath = Path.Combine(workspaceRoot, indexRelative);
        var exportLine = $"export {{ {feature.Pascal}View }} from \"./{feature.Pascal}View\";";
        await AppendLineIfMissingAsync(indexPath, exportLine, ct);
        wired.Add(indexRelative);
    }

    private static async Task WireFastApiRouterAsync(
        string workspaceRoot,
        WorkerScaffoldName feature,
        WorkerScaffoldContext context,
        IReadOnlyList<string> createdFiles,
        List<string> wired,
        List<string> issues,
        CancellationToken ct)
    {
        var routerFile = createdFiles.FirstOrDefault(file =>
            file.StartsWith(context.PythonRouterRoot, StringComparison.OrdinalIgnoreCase) &&
            file.EndsWith($"{feature.Snake}.py", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(routerFile))
        {
            return;
        }

        var appFile = FindFirstExisting(workspaceRoot, ["app/main.py", $"{context.PythonAppRoot}/main.py", "main.py"]);
        if (appFile == null)
        {
            issues.Add("FastAPI router created but no main.py was found for include_router wiring.");
            return;
        }

        var appRelative = Path.GetRelativePath(workspaceRoot, appFile).Replace('\\', '/');
        var modulePath = routerFile[..^3].Replace('/', '.');
        var importLine = $"from {modulePath} import router as {feature.Snake}_router";
        var includeLine = $"app.include_router({feature.Snake}_router)";
        var text = await File.ReadAllTextAsync(appFile, ct);
        if (!text.Contains("FastAPI(", StringComparison.Ordinal) &&
            !text.Contains("app =", StringComparison.Ordinal))
        {
            issues.Add($"{appRelative} does not look like a FastAPI app entrypoint.");
            return;
        }

        text = AddLineAfterImports(text, importLine);
        if (!text.Contains(includeLine, StringComparison.Ordinal))
        {
            text = text.TrimEnd() + Environment.NewLine + includeLine + Environment.NewLine;
        }

        await File.WriteAllTextAsync(appFile, text, new UTF8Encoding(false), ct);
        wired.Add(appRelative);
    }

    private static async Task WireRustModuleAsync(
        string workspaceRoot,
        WorkerScaffoldName feature,
        IReadOnlyList<string> createdFiles,
        List<string> wired,
        List<string> issues,
        CancellationToken ct)
    {
        var rustFile = createdFiles.FirstOrDefault(file =>
            file.EndsWith($"{feature.Snake}.rs", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(rustFile))
        {
            return;
        }

        var libPath = FindFirstExisting(workspaceRoot, ["src/lib.rs"]);
        if (libPath == null)
        {
            issues.Add("Rust module created but src/lib.rs was not found for mod wiring.");
            return;
        }

        var line = $"pub mod {feature.Snake};";
        await AppendLineIfMissingAsync(libPath, line, ct);
        wired.Add("src/lib.rs");
    }

    private static async Task AppendLineIfMissingAsync(string path, string line, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : string.Empty;
        if (text.Contains(line, StringComparison.Ordinal))
        {
            return;
        }

        var next = string.IsNullOrWhiteSpace(text)
            ? line + Environment.NewLine
            : text.TrimEnd() + Environment.NewLine + line + Environment.NewLine;
        await File.WriteAllTextAsync(path, next, new UTF8Encoding(false), ct);
    }

    private static string? FindFirstExisting(string workspaceRoot, IEnumerable<string> candidates)
    {
        return candidates
            .Select(candidate => Path.Combine(workspaceRoot, candidate))
            .FirstOrDefault(File.Exists);
    }

    private static string AddLineAfterImports(string text, string importLine)
    {
        if (text.Contains(importLine, StringComparison.Ordinal))
        {
            return text;
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n').ToList();
        var insertAt = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].StartsWith("import ", StringComparison.Ordinal) ||
                lines[index].StartsWith("from ", StringComparison.Ordinal))
            {
                insertAt = index + 1;
            }
        }

        lines.Insert(insertAt, importLine);
        return string.Join(Environment.NewLine, lines);
    }
}
