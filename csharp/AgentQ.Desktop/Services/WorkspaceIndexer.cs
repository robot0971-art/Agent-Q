using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public sealed partial class WorkspaceIndexer
{
    private const int MaximumFilesInTree = 300;
    private const int MaximumIncludedFiles = 24;
    private const int MaximumFileChars = 6000;
    private const int MaximumTotalChars = 60000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".csproj", ".sln", ".props", ".targets",
        ".json", ".md", ".txt", ".xml", ".yml", ".yaml",
        ".ps1", ".cmd", ".bat", ".sh", ".js", ".ts", ".tsx", ".jsx",
        ".html", ".css", ".scss", ".py", ".go", ".rs", ".java", ".kt"
    };

    private static readonly string[] PriorityFileNames =
    [
        "README.md", "readme.md", "package.json", "global.json",
        "Directory.Build.props", "Directory.Build.targets",
        ".editorconfig", ".gitignore", ".gitattributes"
    ];

    public Task<string> BuildContextAsync(string workspaceRoot, CancellationToken ct) =>
        BuildContextAsync(workspaceRoot, query: string.Empty, ct);

    public async Task<string> BuildContextAsync(string workspaceRoot, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return string.Empty;
        }

        var root = Path.GetFullPath(workspaceRoot);
        var queryTerms = ExtractQueryTerms(query);
        var files = SafeEnumerateFiles(root)
            .Where(file => !IsExcludedPath(root, file))
            .Select(file => new WorkspaceFile(file, Path.GetRelativePath(root, file).Replace('\\', '/')))
            .OrderByDescending(file => ScoreQueryMatch(file.RelativePath, queryTerms))
            .ThenBy(file => GetPriority(file.RelativePath))
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumFilesInTree)
            .ToList();

        if (files.Count == 0)
        {
            return BuildEmptyWorkspaceContext(root, query);
        }

        var builder = new StringBuilder();
        builder.AppendLine("Workspace context snapshot:");
        builder.AppendLine($"Root: {root}");
        if (queryTerms.Count > 0)
        {
            builder.AppendLine($"Query-aware priority terms: {string.Join(", ", queryTerms.Take(8))}");
        }

        builder.AppendLine();
        builder.AppendLine("File tree:");
        foreach (var file in files)
        {
            builder.AppendLine($"- {file.RelativePath}");
        }

        builder.AppendLine();
        builder.AppendLine("Selected file contents:");

        var included = 0;
        foreach (var file in files.Where(IsReadableTextFile).Take(MaximumIncludedFiles))
        {
            ct.ThrowIfCancellationRequested();

            var content = await ReadTextFileAsync(file.FullPath, ct);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (content.Length > MaximumFileChars)
            {
                content = content[..MaximumFileChars] + "\n[truncated]";
            }

            if (builder.Length + content.Length > MaximumTotalChars)
            {
                builder.AppendLine("[workspace context truncated]");
                break;
            }

            builder.AppendLine();
            builder.AppendLine($"--- {file.RelativePath} ---");
            builder.AppendLine(content);
            included++;
        }

        return included == 0 ? builder.ToString().Trim() : builder.ToString().TrimEnd();
    }

    private static async Task<string> ReadTextFileAsync(string path, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(path, ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsReadableTextFile(WorkspaceFile file)
    {
        var extension = Path.GetExtension(file.FullPath);
        return TextExtensions.Contains(extension) ||
               PriorityFileNames.Contains(Path.GetFileName(file.FullPath), StringComparer.OrdinalIgnoreCase);
    }

    private static int GetPriority(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        if (PriorityFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        var extension = Path.GetExtension(relativePath);
        if (extension is ".sln" or ".csproj" or ".md")
        {
            return 1;
        }

        if (relativePath.Contains("/Services/", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains("/ViewModels/", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return TextExtensions.Contains(extension) ? 3 : 9;
    }

    private static IReadOnlyList<string> ExtractQueryTerms(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return QueryTermRegex()
            .Matches(query.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(term => term.Length >= 3 && !IsStopWord(term))
            .Distinct()
            .Take(16)
            .ToList();
    }

    private static int ScoreQueryMatch(string relativePath, IReadOnlyList<string> queryTerms)
    {
        if (queryTerms.Count == 0)
        {
            return 0;
        }

        var normalized = relativePath.ToLowerInvariant();
        return queryTerms.Count(term => normalized.Contains(term, StringComparison.Ordinal));
    }

    private static bool IsStopWord(string value)
    {
        return value is "the" or "and" or "for" or "with" or "this" or "that" or "from" or "into" or "file" or "code";
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (IsExcludedDirectory(directory))
                {
                    continue;
                }

                pending.Push(directory);
            }
        }
    }

    private static bool IsExcludedDirectory(string directory)
    {
        var name = Path.GetFileName(directory);
        return name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".agentq", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("artifacts", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedPath(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(part =>
            part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".agentq", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("artifacts", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildEmptyWorkspaceContext(string root, string query)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Workspace context snapshot:");
        builder.AppendLine($"Root: {root}");
        builder.AppendLine("No user project files were found after excluding AgentQ metadata and build/tool folders.");
        builder.AppendLine("Empty-workspace bootstrap guidance:");
        builder.AppendLine("- Treat requests to make a website, portfolio, app, game, API, or project as greenfield only when the product type or concrete stack is named.");
        builder.AppendLine("- If the user only says they want a new project, ask what kind of project they want before choosing a stack or creating files.");
        builder.AppendLine("- Do not say you will create a specific starter until the product type or stack is clear enough to act.");
        builder.AppendLine("- Choose a common default stack only for implementation details after the user has specified the product direction.");
        builder.AppendLine("- User corrections override defaults: if JavaScript is requested after TypeScript was recommended, scaffold JavaScript files, not TypeScript.");
        builder.AppendLine("- For a portfolio or website request with no language preference, prefer Vite + React + JavaScript with package.json, .jsx/.js src files, CSS, and README, then run npm install/build when available.");
        builder.AppendLine("- Use TypeScript for a portfolio or website only when the user explicitly asks for TypeScript or an existing project already uses TypeScript.");
        builder.AppendLine("- If minor implementation details are missing after the product direction is clear, make a reasonable default explicit in the final summary and keep the scaffold easy to revise.");
        if (!string.IsNullOrWhiteSpace(query))
        {
            builder.AppendLine($"User request: {query.Trim()}");
        }

        return builder.ToString().TrimEnd();
    }

    private sealed record WorkspaceFile(string FullPath, string RelativePath);

    [GeneratedRegex("[a-z0-9_]+", RegexOptions.CultureInvariant)]
    private static partial Regex QueryTermRegex();
}
