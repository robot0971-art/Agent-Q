using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AgentQ.Desktop.Services;

public sealed partial class WorkspaceDependencyGraphService
{
    private const int MaximumFiles = 800;
    private const long MaximumFileBytes = 512 * 1024;

    private static readonly string[] JavaScriptExtensions = [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs"];

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "artifacts",
        ".codex-build",
        ".venv",
        "venv",
        "env",
        "__pycache__",
        ".agentq",
        "dist",
        "build",
        ".next",
        "coverage"
    };

    public WorkspaceDependencyGraph Build(string workspaceRoot)
    {
        var graph = new WorkspaceDependencyGraph();
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return graph;
        }

        var root = Path.GetFullPath(workspaceRoot);
        var files = EnumerateSupportedFiles(root)
            .Where(file => TryGetFileInfo(file, out var length) && length <= MaximumFileBytes)
            .Take(MaximumFiles)
            .ToList();
        var knownFiles = files
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            graph.FilesIndexed++;
            AddEdgesForFile(root, file, knownFiles, graph.Edges);
        }

        Deduplicate(graph.Edges);
        return graph;
    }

    private static void AddEdgesForFile(
        string root,
        string file,
        HashSet<string> knownFiles,
        List<WorkspaceDependencyEdge> edges)
    {
        var extension = Path.GetExtension(file);
        if (JavaScriptExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            AddJavaScriptEdges(root, file, knownFiles, edges);
            return;
        }

        if (extension.Equals(".py", StringComparison.OrdinalIgnoreCase))
        {
            AddPythonEdges(root, file, knownFiles, edges);
            return;
        }

        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            AddCSharpUsingEdges(root, file, edges);
            return;
        }

        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            AddCSharpProjectEdges(root, file, knownFiles, edges);
        }
    }

    private static void AddJavaScriptEdges(
        string root,
        string file,
        HashSet<string> knownFiles,
        List<WorkspaceDependencyEdge> edges)
    {
        var relativePath = Relative(root, file);
        foreach (var (line, source) in ReadLines(file).Select((value, index) => (index + 1, value)))
        {
            var match = JavaScriptImportRegex().Match(source);
            if (!match.Success)
            {
                match = JavaScriptExportFromRegex().Match(source);
            }

            if (!match.Success)
            {
                continue;
            }

            var target = match.Groups["source"].Value;
            var resolved = ResolveJavaScriptTarget(root, relativePath, target, knownFiles);
            edges.Add(new WorkspaceDependencyEdge
            {
                FromPath = relativePath,
                ToPath = resolved,
                Target = target,
                Kind = "import",
                Language = "JavaScript/TypeScript",
                Line = line
            });
        }
    }

    private static void AddPythonEdges(
        string root,
        string file,
        HashSet<string> knownFiles,
        List<WorkspaceDependencyEdge> edges)
    {
        var relativePath = Relative(root, file);
        foreach (var (line, source) in ReadLines(file).Select((value, index) => (index + 1, value.Trim())))
        {
            var match = PythonImportRegex().Match(source);
            if (match.Success)
            {
                AddPythonModuleEdge(root, relativePath, match.Groups["module"].Value, "import", line, knownFiles, edges);
                continue;
            }

            match = PythonFromImportRegex().Match(source);
            if (match.Success)
            {
                var module = match.Groups["module"].Value;
                if (string.IsNullOrWhiteSpace(module))
                {
                    module = match.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                }

                AddPythonModuleEdge(root, relativePath, module, "from-import", line, knownFiles, edges);
            }
        }
    }

    private static void AddCSharpUsingEdges(string root, string file, List<WorkspaceDependencyEdge> edges)
    {
        var relativePath = Relative(root, file);
        foreach (var (line, source) in ReadLines(file).Select((value, index) => (index + 1, value.Trim())))
        {
            var match = CSharpUsingRegex().Match(source);
            if (!match.Success)
            {
                continue;
            }

            edges.Add(new WorkspaceDependencyEdge
            {
                FromPath = relativePath,
                Target = match.Groups["namespace"].Value,
                Kind = "using",
                Language = "C#",
                Line = line
            });
        }
    }

    private static void AddCSharpProjectEdges(
        string root,
        string file,
        HashSet<string> knownFiles,
        List<WorkspaceDependencyEdge> edges)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(file);
        }
        catch
        {
            return;
        }

        var relativePath = Relative(root, file);
        foreach (var reference in document.Descendants()
                     .Where(element => element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase))
                     .Select(element => element.Attribute("Include")?.Value)
                     .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var combined = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file) ?? root, reference!));
            var target = IsInsideRoot(root, combined)
                ? Relative(root, combined)
                : reference!.Replace('\\', '/');
            edges.Add(new WorkspaceDependencyEdge
            {
                FromPath = relativePath,
                ToPath = knownFiles.Contains(target) ? target : string.Empty,
                Target = target,
                Kind = "project-reference",
                Language = "C#"
            });
        }
    }

    private static void AddPythonModuleEdge(
        string root,
        string relativePath,
        string module,
        string kind,
        int line,
        HashSet<string> knownFiles,
        List<WorkspaceDependencyEdge> edges)
    {
        if (string.IsNullOrWhiteSpace(module))
        {
            return;
        }

        var resolved = ResolvePythonTarget(module, knownFiles);
        edges.Add(new WorkspaceDependencyEdge
        {
            FromPath = relativePath,
            ToPath = resolved,
            Target = module,
            Kind = kind,
            Language = "Python",
            Line = line
        });
    }

    private static string ResolveJavaScriptTarget(
        string root,
        string fromRelativePath,
        string target,
        HashSet<string> knownFiles)
    {
        if (!target.StartsWith(".", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var fromDirectory = Path.GetDirectoryName(fromRelativePath)?.Replace('\\', '/') ?? string.Empty;
        var basePath = Path.GetFullPath(Path.Combine(root, fromDirectory, target));
        var candidates = JavaScriptExtensions
            .Select(extension => $"{basePath}{extension}")
            .Concat(JavaScriptExtensions.Select(extension => Path.Combine(basePath, $"index{extension}")));

        foreach (var candidate in candidates)
        {
            if (!IsInsideRoot(root, candidate))
            {
                continue;
            }

            var relative = Relative(root, candidate);
            if (knownFiles.Contains(relative))
            {
                return relative;
            }
        }

        return string.Empty;
    }

    private static string ResolvePythonTarget(string module, HashSet<string> knownFiles)
    {
        var normalized = module.TrimStart('.').Replace('.', '/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var candidates = new[] { $"{normalized}.py", $"{normalized}/__init__.py" };
        foreach (var candidate in candidates)
        {
            var match = knownFiles.FirstOrDefault(file =>
                file.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith($"/{candidate}", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> EnumerateSupportedFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current)
                    .Where(IsSupportedFile);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(directory)))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static bool IsSupportedFile(string file)
    {
        var extension = Path.GetExtension(file);
        return JavaScriptExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               extension.Equals(".py", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ReadLines(string file)
    {
        try
        {
            return File.ReadLines(file);
        }
        catch
        {
            return [];
        }
    }

    private static bool TryGetFileInfo(string file, out long length)
    {
        try
        {
            length = new FileInfo(file).Length;
            return true;
        }
        catch
        {
            length = 0;
            return false;
        }
    }

    private static bool IsInsideRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        return !relative.StartsWith("..", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static string Relative(string root, string path)
    {
        return Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
    }

    private static void Deduplicate(List<WorkspaceDependencyEdge> edges)
    {
        var unique = edges
            .GroupBy(edge => new
            {
                edge.FromPath,
                edge.ToPath,
                edge.Target,
                edge.Kind,
                edge.Language,
                edge.Line
            })
            .Select(group => group.First())
            .OrderBy(edge => edge.FromPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.Line)
            .ToList();

        edges.Clear();
        edges.AddRange(unique);
    }

    [GeneratedRegex("""^\s*import\s+(?:.+?\s+from\s+)?["'](?<source>[^"']+)["']""")]
    private static partial Regex JavaScriptImportRegex();

    [GeneratedRegex("""^\s*export\s+.+?\s+from\s+["'](?<source>[^"']+)["']""")]
    private static partial Regex JavaScriptExportFromRegex();

    [GeneratedRegex("""^\s*import\s+(?<module>[A-Za-z_][\w.]*)""")]
    private static partial Regex PythonImportRegex();

    [GeneratedRegex("""^\s*from\s+(?<module>[.\w]*)\s+import\s+(?<names>[\w,\s*]+)""")]
    private static partial Regex PythonFromImportRegex();

    [GeneratedRegex("""^\s*using\s+(?:static\s+)?(?<namespace>[A-Za-z_][\w.]*);""")]
    private static partial Regex CSharpUsingRegex();
}
