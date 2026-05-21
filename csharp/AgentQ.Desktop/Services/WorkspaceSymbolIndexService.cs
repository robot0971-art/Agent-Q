using System.IO;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public sealed partial class WorkspaceSymbolIndexService
{
    private const int MaximumFiles = 400;
    private const long MaximumFileBytes = 512 * 1024;

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
        "__pycache__"
    };

    public WorkspaceSymbolIndex Build(string workspaceRoot)
    {
        var index = new WorkspaceSymbolIndex();
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return index;
        }

        foreach (var file in SafeEnumerateFiles(workspaceRoot, "*.cs").Take(MaximumFiles))
        {
            if (!TryGetFileInfo(file, out var length) || length > MaximumFileBytes)
            {
                continue;
            }

            index.FilesIndexed++;
            AddCSharpSymbols(workspaceRoot, file, index.Symbols);
        }

        return index;
    }

    private static void AddCSharpSymbols(string workspaceRoot, string file, List<CodeSymbol> symbols)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch
        {
            return;
        }

        var relativePath = Path.GetRelativePath(workspaceRoot, file);
        string? currentType = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var typeMatch = CSharpTypeRegex().Match(line);
            if (typeMatch.Success)
            {
                currentType = typeMatch.Groups["name"].Value;
                symbols.Add(new CodeSymbol
                {
                    Name = currentType,
                    Kind = typeMatch.Groups["kind"].Value,
                    Language = "C#",
                    RelativePath = relativePath,
                    Line = i + 1
                });
                continue;
            }

            var methodMatch = CSharpMethodRegex().Match(line);
            if (methodMatch.Success)
            {
                var methodName = methodMatch.Groups["name"].Value;
                if (IsIgnoredMethodLikeToken(methodName))
                {
                    continue;
                }

                symbols.Add(new CodeSymbol
                {
                    Name = methodName,
                    Kind = "method",
                    Language = "C#",
                    RelativePath = relativePath,
                    Line = i + 1,
                    Container = currentType
                });
            }
        }
    }

    private static bool IsIgnoredMethodLikeToken(string value) =>
        value is "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "lock";

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

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, pattern);
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

    [GeneratedRegex("""\b(?<kind>class|record|struct|interface|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)""")]
    private static partial Regex CSharpTypeRegex();

    [GeneratedRegex("""\b(?:public|private|protected|internal|static|virtual|override|sealed|async|partial|extern|\s)+\s*(?:[\w<>\[\],\?\.]+\s+)+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(""")]
    private static partial Regex CSharpMethodRegex();
}
