using System.IO;
using System.Text;

namespace AgentQ.Desktop.Services;

public sealed class WorkerScaffoldAutoWirer
{
    public async Task<IReadOnlyList<WorkerScaffoldWiringChange>> WireAsync(
        string workspaceRoot,
        WorkerPlan plan,
        WorkerScaffoldName feature,
        WorkerScaffoldContext context,
        IReadOnlyList<string> createdFiles,
        List<string> issues,
        CancellationToken ct = default)
    {
        var changes = new List<WorkerScaffoldWiringChange>();
        if (createdFiles.Count == 0)
        {
            return changes;
        }

        if (plan.Language.Contains("typescript", StringComparison.OrdinalIgnoreCase) ||
            plan.Framework.Contains("react", StringComparison.OrdinalIgnoreCase))
        {
            await WireReactIndexAsync(workspaceRoot, feature, createdFiles, changes, ct);
        }

        if (plan.Framework.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
        {
            await WireFastApiRouterAsync(workspaceRoot, feature, context, createdFiles, changes, issues, ct);
        }

        if (plan.Language.Contains("rust", StringComparison.OrdinalIgnoreCase))
        {
            await WireRustModuleAsync(workspaceRoot, feature, createdFiles, changes, issues, ct);
        }

        return changes;
    }

    private static async Task WireReactIndexAsync(
        string workspaceRoot,
        WorkerScaffoldName feature,
        IReadOnlyList<string> createdFiles,
        List<WorkerScaffoldWiringChange> changes,
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
        var change = await AppendLineIfMissingAsync(
            indexPath,
            indexRelative,
            exportLine,
            $"Export {feature.Pascal}View from feature barrel.",
            ct);
        if (change != null)
        {
            changes.Add(change);
        }
    }

    private static async Task WireFastApiRouterAsync(
        string workspaceRoot,
        WorkerScaffoldName feature,
        WorkerScaffoldContext context,
        IReadOnlyList<string> createdFiles,
        List<WorkerScaffoldWiringChange> changes,
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
        var before = await File.ReadAllTextAsync(appFile, ct);
        if (!before.Contains("FastAPI(", StringComparison.Ordinal) &&
            !before.Contains("app =", StringComparison.Ordinal))
        {
            issues.Add($"{appRelative} does not look like a FastAPI app entrypoint.");
            return;
        }

        var after = AddLineAfterImports(before, importLine);
        if (!after.Contains(includeLine, StringComparison.Ordinal))
        {
            after = after.TrimEnd() + Environment.NewLine + includeLine + Environment.NewLine;
        }

        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(appFile, after, new UTF8Encoding(false), ct);
            changes.Add(new WorkerScaffoldWiringChange
            {
                Path = appRelative,
                Before = before,
                After = after,
                Summary = $"Register FastAPI router {feature.Snake}_router."
            });
        }
    }

    private static async Task WireRustModuleAsync(
        string workspaceRoot,
        WorkerScaffoldName feature,
        IReadOnlyList<string> createdFiles,
        List<WorkerScaffoldWiringChange> changes,
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
        var change = await AppendLineIfMissingAsync(
            libPath,
            "src/lib.rs",
            line,
            $"Expose Rust module {feature.Snake}.",
            ct);
        if (change != null)
        {
            changes.Add(change);
        }
    }

    private static async Task<WorkerScaffoldWiringChange?> AppendLineIfMissingAsync(
        string path,
        string relativePath,
        string line,
        string summary,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var before = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : string.Empty;
        if (before.Contains(line, StringComparison.Ordinal))
        {
            return null;
        }

        var after = string.IsNullOrWhiteSpace(before)
            ? line + Environment.NewLine
            : before.TrimEnd() + Environment.NewLine + line + Environment.NewLine;
        await File.WriteAllTextAsync(path, after, new UTF8Encoding(false), ct);
        return new WorkerScaffoldWiringChange
        {
            Path = relativePath.Replace('\\', '/'),
            Before = before,
            After = after,
            Summary = summary
        };
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
