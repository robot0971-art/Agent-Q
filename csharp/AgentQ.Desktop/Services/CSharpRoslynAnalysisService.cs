using System.IO;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AgentQ.Desktop.Services;

public sealed class CSharpRoslynAnalysisService
{
    private const int MaximumFiles = 300;
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

    public CSharpRoslynAnalysis Analyze(string workspaceRoot)
    {
        var analysis = new CSharpRoslynAnalysis();
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return analysis;
        }

        var root = Path.GetFullPath(workspaceRoot);
        foreach (var project in SafeEnumerateFiles(root, "*.csproj").Take(50))
        {
            AnalyzeProject(root, project, analysis);
        }

        foreach (var file in SafeEnumerateFiles(root, "*.cs").Take(MaximumFiles))
        {
            if (!TryGetFileInfo(file, out var length) || length > MaximumFileBytes)
            {
                continue;
            }

            AnalyzeFile(root, file, analysis);
        }

        Deduplicate(analysis);
        return analysis;
    }

    private static void AnalyzeProject(string root, string project, CSharpRoslynAnalysis analysis)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(project);
        }
        catch
        {
            return;
        }

        var relativeProject = Relative(root, project);
        analysis.Projects.Add(relativeProject);
        foreach (var reference in document.Descendants()
                     .Where(element => element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase))
                     .Select(element => element.Attribute("Include")?.Value)
                     .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var combined = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project) ?? root, reference!));
            analysis.ProjectReferences.Add(new CSharpRoslynProjectReference
            {
                Path = relativeProject,
                Target = IsInsideRoot(root, combined) ? Relative(root, combined) : reference!.Replace('\\', '/')
            });
        }
    }

    private static void AnalyzeFile(string root, string file, CSharpRoslynAnalysis analysis)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch
        {
            return;
        }

        var relative = Relative(root, file);
        var tree = CSharpSyntaxTree.ParseText(text, path: file);
        var syntaxRoot = tree.GetCompilationUnitRoot();
        analysis.FilesIndexed++;

        foreach (var diagnostic in tree.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).Take(5))
        {
            var location = diagnostic.Location.GetLineSpan().StartLinePosition;
            analysis.Diagnostics.Add(new CSharpRoslynDiagnostic
            {
                Path = relative,
                Line = location.Line + 1,
                Id = diagnostic.Id,
                Message = diagnostic.GetMessage()
            });
        }

        foreach (var usingDirective in syntaxRoot.Usings)
        {
            analysis.Usings.Add(new CSharpRoslynUsing
            {
                Path = relative,
                Line = LineOf(usingDirective),
                Namespace = usingDirective.Name?.ToString() ?? string.Empty
            });
        }

        foreach (var namespaceDeclaration in syntaxRoot.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            analysis.Namespaces.Add(new CSharpRoslynNamespace
            {
                Path = relative,
                Line = LineOf(namespaceDeclaration),
                Name = namespaceDeclaration.Name.ToString()
            });
        }

        foreach (var type in syntaxRoot.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            analysis.Symbols.Add(new CSharpRoslynSymbol
            {
                Path = relative,
                Line = LineOf(type),
                Kind = TypeKindOf(type),
                Name = type.Identifier.ValueText,
                Container = ContainingTypeName(type),
                Namespace = ContainingNamespaceName(type)
            });
        }

        foreach (var method in syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            analysis.Symbols.Add(new CSharpRoslynSymbol
            {
                Path = relative,
                Line = LineOf(method),
                Kind = "method",
                Name = method.Identifier.ValueText,
                Container = ContainingTypeName(method),
                Namespace = ContainingNamespaceName(method)
            });
        }

        foreach (var constructor in syntaxRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            analysis.Symbols.Add(new CSharpRoslynSymbol
            {
                Path = relative,
                Line = LineOf(constructor),
                Kind = "constructor",
                Name = constructor.Identifier.ValueText,
                Container = ContainingTypeName(constructor),
                Namespace = ContainingNamespaceName(constructor)
            });
        }
    }

    private static string? ContainingTypeName(SyntaxNode node) =>
        node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;

    private static string TypeKindOf(BaseTypeDeclarationSyntax type) =>
        type switch
        {
            ClassDeclarationSyntax => "class",
            RecordDeclarationSyntax => "record",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            EnumDeclarationSyntax => "enum",
            _ => "type"
        };

    private static string ContainingNamespaceName(SyntaxNode node) =>
        node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? string.Empty;

    private static int LineOf(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

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

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');

    private static void Deduplicate(CSharpRoslynAnalysis analysis)
    {
        analysis.Projects = analysis.Projects.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        analysis.Namespaces = analysis.Namespaces.DistinctBy(item => $"{item.Path}|{item.Line}|{item.Name}").ToList();
        analysis.Symbols = analysis.Symbols.DistinctBy(item => $"{item.Path}|{item.Line}|{item.Kind}|{item.Name}|{item.Container}").ToList();
        analysis.Usings = analysis.Usings.DistinctBy(item => $"{item.Path}|{item.Line}|{item.Namespace}").ToList();
        analysis.ProjectReferences = analysis.ProjectReferences.DistinctBy(item => $"{item.Path}|{item.Target}").ToList();
        analysis.Diagnostics = analysis.Diagnostics.DistinctBy(item => $"{item.Path}|{item.Line}|{item.Id}").ToList();
    }
}

public sealed class CSharpRoslynAnalysis
{
    public int FilesIndexed { get; set; }

    public List<string> Projects { get; set; } = [];

    public List<CSharpRoslynNamespace> Namespaces { get; set; } = [];

    public List<CSharpRoslynSymbol> Symbols { get; set; } = [];

    public List<CSharpRoslynUsing> Usings { get; set; } = [];

    public List<CSharpRoslynProjectReference> ProjectReferences { get; set; } = [];

    public List<CSharpRoslynDiagnostic> Diagnostics { get; set; } = [];
}

public sealed class CSharpRoslynNamespace
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class CSharpRoslynSymbol
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Container { get; set; }

    public string Namespace { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Container)
        ? $"{Kind} {Name} ({Path}:{Line:0})"
        : $"{Kind} {Container}.{Name} ({Path}:{Line:0})";
}

public sealed class CSharpRoslynUsing
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Namespace { get; set; } = string.Empty;
}

public sealed class CSharpRoslynProjectReference
{
    public string Path { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;
}

public sealed class CSharpRoslynDiagnostic
{
    public string Path { get; set; } = string.Empty;

    public int Line { get; set; }

    public string Id { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
