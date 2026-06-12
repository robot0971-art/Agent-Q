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
        ".agentq",
        ".agents",
        ".codex",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "artifacts",
        ".codex-build",
        ".agentq-verify",
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

        foreach (var file in SafeEnumerateCodeFiles(workspaceRoot).Take(MaximumFiles))
        {
            if (!TryGetFileInfo(file, out var length) || length > MaximumFileBytes)
            {
                continue;
            }

            index.FilesIndexed++;
            AddSymbolsForFile(workspaceRoot, file, index.Symbols);
        }

        return index;
    }

    public IReadOnlyList<CodeSymbol> BuildForFile(string workspaceRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        var fullPath = Path.IsPathFullyQualified(path)
            ? path
            : Path.Combine(workspaceRoot, path);

        if (!File.Exists(fullPath) ||
            !IsSupportedCodeFile(fullPath) ||
            !IsInsideWorkspace(workspaceRoot, fullPath) ||
            !TryGetFileInfo(fullPath, out var length) ||
            length > MaximumFileBytes)
        {
            return [];
        }

        var symbols = new List<CodeSymbol>();
        AddSymbolsForFile(Path.GetFullPath(workspaceRoot), Path.GetFullPath(fullPath), symbols);
        return symbols;
    }

    private static void AddSymbolsForFile(string workspaceRoot, string file, List<CodeSymbol> symbols)
    {
        var extension = Path.GetExtension(file);
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            AddCSharpSymbols(workspaceRoot, file, symbols);
            return;
        }

        if (extension.Equals(".py", StringComparison.OrdinalIgnoreCase))
        {
            AddPythonSymbols(workspaceRoot, file, symbols);
            return;
        }

        if (extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase))
        {
            AddJavaScriptSymbols(workspaceRoot, file, symbols);
        }
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

    private static void AddPythonSymbols(string workspaceRoot, string file, List<CodeSymbol> symbols)
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
        string? currentClass = null;
        var currentClassIndent = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = rawLine.Length - rawLine.TrimStart().Length;
            if (currentClassIndent >= 0 && indent <= currentClassIndent)
            {
                currentClass = null;
                currentClassIndent = -1;
            }

            var classMatch = PythonClassRegex().Match(line);
            if (classMatch.Success)
            {
                currentClass = classMatch.Groups["name"].Value;
                currentClassIndent = indent;
                symbols.Add(new CodeSymbol
                {
                    Name = currentClass,
                    Kind = "class",
                    Language = "Python",
                    RelativePath = relativePath,
                    Line = i + 1
                });
                continue;
            }

            var functionMatch = PythonFunctionRegex().Match(line);
            if (functionMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = functionMatch.Groups["name"].Value,
                    Kind = "function",
                    Language = "Python",
                    RelativePath = relativePath,
                    Line = i + 1,
                    Container = currentClass
                });
            }
        }
    }

    private static void AddJavaScriptSymbols(string workspaceRoot, string file, List<CodeSymbol> symbols)
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
        var language = Path.GetExtension(file).Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetExtension(file).Equals(".jsx", StringComparison.OrdinalIgnoreCase)
            ? "JavaScript"
            : "TypeScript";
        string? currentClass = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var classMatch = JavaScriptClassRegex().Match(line);
            if (classMatch.Success)
            {
                currentClass = classMatch.Groups["name"].Value;
                symbols.Add(new CodeSymbol
                {
                    Name = currentClass,
                    Kind = "class",
                    Language = language,
                    RelativePath = relativePath,
                    Line = i + 1
                });
                continue;
            }

            var functionMatch = JavaScriptFunctionRegex().Match(line);
            if (!functionMatch.Success)
            {
                functionMatch = JavaScriptArrowFunctionRegex().Match(line);
            }

            if (functionMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = functionMatch.Groups["name"].Value,
                    Kind = "function",
                    Language = language,
                    RelativePath = relativePath,
                    Line = i + 1,
                    Container = IsLikelyClassMethod(line) ? currentClass : null
                });
            }
        }
    }

    private static bool IsLikelyClassMethod(string line) =>
        !line.Contains("function", StringComparison.Ordinal) &&
        !line.Contains("=>", StringComparison.Ordinal) &&
        !line.StartsWith("export ", StringComparison.Ordinal) &&
        !line.StartsWith("const ", StringComparison.Ordinal) &&
        !line.StartsWith("let ", StringComparison.Ordinal);

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

    private static bool IsInsideWorkspace(string workspaceRoot, string path)
    {
        try
        {
            return WorkspacePathResolver.IsResolvedInsideWorkspace(workspaceRoot, path);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> SafeEnumerateCodeFiles(string root)
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
                    .Where(file => WorkspacePathResolver.IsResolvedInsideWorkspace(root, file) &&
                                   IsSupportedCodeFile(file));
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
                if (!ExcludedDirectories.Contains(Path.GetFileName(directory)) &&
                    !IsReparseDirectory(directory) &&
                    WorkspacePathResolver.IsResolvedInsideWorkspace(root, directory))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static bool IsReparseDirectory(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsSupportedCodeFile(string file)
    {
        var extension = Path.GetExtension(file);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".py", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("""\b(?<kind>class|record|struct|interface|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)""")]
    private static partial Regex CSharpTypeRegex();

    [GeneratedRegex("""\b(?:public|private|protected|internal|static|virtual|override|sealed|async|partial|extern|\s)+\s*(?:[\w<>\[\],\?\.]+\s+)+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(""")]
    private static partial Regex CSharpMethodRegex();

    [GeneratedRegex("""^class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)""")]
    private static partial Regex PythonClassRegex();

    [GeneratedRegex("""^(?:async\s+)?def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(""")]
    private static partial Regex PythonFunctionRegex();

    [GeneratedRegex("""^(?:export\s+default\s+|export\s+)?class\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)""")]
    private static partial Regex JavaScriptClassRegex();

    [GeneratedRegex("""^(?:export\s+)?(?:async\s+)?function\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*\(|^(?:public\s+|private\s+|protected\s+|async\s+|static\s+)*(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*\(""")]
    private static partial Regex JavaScriptFunctionRegex();

    [GeneratedRegex("""^(?:export\s+)?(?:const|let|var)\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*(?:async\s*)?(?:\([^)]*\)|[A-Za-z_$][A-Za-z0-9_$]*)\s*=>""")]
    private static partial Regex JavaScriptArrowFunctionRegex();
}
