using System.IO;
using System.Text;

namespace AgentQ.Desktop.Services;

public sealed class WorkspaceIndexer
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
        "Directory.Build.props", "Directory.Build.targets"
    ];

    public async Task<string> BuildContextAsync(string workspaceRoot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return string.Empty;
        }

        var root = Path.GetFullPath(workspaceRoot);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => !IsExcludedPath(root, file))
            .Select(file => new WorkspaceFile(file, Path.GetRelativePath(root, file).Replace('\\', '/')))
            .OrderBy(file => GetPriority(file.RelativePath))
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumFilesInTree)
            .ToList();

        if (files.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Workspace context snapshot:");
        builder.AppendLine($"Root: {root}");
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
        return TextExtensions.Contains(extension) || PriorityFileNames.Contains(Path.GetFileName(file.FullPath));
    }

    private static int GetPriority(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        if (PriorityFileNames.Contains(fileName))
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

    private static bool IsExcludedPath(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(part =>
            part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("artifacts", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record WorkspaceFile(string FullPath, string RelativePath);
}
