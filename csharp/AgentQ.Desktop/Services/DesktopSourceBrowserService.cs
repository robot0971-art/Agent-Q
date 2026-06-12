using System.IO;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopSourceBrowserService
{
    private const int MaximumFiles = 300;
    private const int MaximumPreviewBytes = 256 * 1024;

    private static readonly string[] ExcludedDirectories =
    [
        ".git",
        ".agentq",
        ".agents",
        ".codex",
        ".codex-build",
        ".vs",
        ".vscode",
        "bin",
        "obj",
        "Library",
        "Temp",
        "Logs",
        "artifacts",
        "node_modules",
        "dist",
        "build"
    ];

    private static readonly string[] PreferredExtensions =
    [
        ".cs",
        ".xaml",
        ".csproj",
        ".sln",
        ".json",
        ".md",
        ".txt",
        ".xml",
        ".yml",
        ".yaml",
        ".props",
        ".targets",
        ".ts",
        ".tsx",
        ".js",
        ".jsx",
        ".css",
        ".html",
        ".py",
        ".go",
        ".rs",
        ".cpp",
        ".h",
        ".hpp"
    ];

    public void Refresh(MainViewModel viewModel)
    {
        viewModel.SourceFiles.Clear();
        viewModel.SelectedSourceFile = null;
        viewModel.SourceFilePreviewText = string.Empty;

        if (!Directory.Exists(viewModel.WorkspaceRoot))
        {
            viewModel.StatusText = DesktopLocalizer.UiText(DesktopText.NoValidProjectFolderToOpen, viewModel.IsKoreanUi);
            return;
        }

        var root = Path.GetFullPath(viewModel.WorkspaceRoot);
        var filter = viewModel.SourceFileFilter.Trim();
        foreach (var entry in BuildSourceTree(root, filter))
        {
            viewModel.SourceFiles.Add(entry);
        }

        var fileCount = viewModel.SourceFiles.Sum(entry => entry.IsDirectory ? entry.FileCount : 1);
        viewModel.StatusText = viewModel.IsKoreanUi
            ? $"\uD30C\uC77C {fileCount:0}\uAC1C\uB97C \uBD88\uB7EC\uC654\uC2B5\uB2C8\uB2E4"
            : $"Loaded {fileCount:0} files";
        viewModel.AddLog(viewModel.StatusText);
    }

    public async Task OpenSelectedAsync(MainViewModel viewModel, CancellationToken ct = default)
    {
        var file = viewModel.SelectedSourceFile;
        if (file == null)
        {
            viewModel.SourceFilePreviewText = viewModel.IsKoreanUi
                ? "\uD30C\uC77C\uC744 \uC120\uD0DD\uD558\uBA74 \uC5EC\uAE30\uC5D0 \uCF54\uB4DC\uAC00 \uD45C\uC2DC\uB429\uB2C8\uB2E4."
                : "Select a file to preview its source.";
            return;
        }

        if (file.IsDirectory)
        {
            viewModel.SourceFilePreviewText = viewModel.IsKoreanUi
                ? $"\uD3F4\uB354: {file.RelativePath}"
                : $"Folder: {file.RelativePath}";
            return;
        }

        if (!IsUnderWorkspace(viewModel.WorkspaceRoot, file.FullPath) || !File.Exists(file.FullPath))
        {
            viewModel.SourceFilePreviewText = viewModel.IsKoreanUi
                ? "\uC6CC\uD06C\uC2A4\uD398\uC774\uC2A4 \uC548\uC758 \uC720\uD6A8\uD55C \uD30C\uC77C\uC774 \uC544\uB2D9\uB2C8\uB2E4."
                : "This is not a valid file inside the workspace.";
            return;
        }

        var info = new FileInfo(file.FullPath);
        if (info.Length > MaximumPreviewBytes)
        {
            viewModel.SourceFilePreviewText = viewModel.IsKoreanUi
                ? $"\uD30C\uC77C\uC774 \uB108\uBB34 \uCEE4\uC11C \uBBF8\uB9AC\uBCF4\uAE30\uB97C \uC0DD\uB7B5\uD588\uC2B5\uB2C8\uB2E4. ({info.Length:N0} bytes)"
                : $"File is too large to preview. ({info.Length:N0} bytes)";
            return;
        }

        viewModel.SourceFilePreviewText = await File.ReadAllTextAsync(file.FullPath, ct);
        viewModel.StatusText = viewModel.IsKoreanUi
            ? $"\uD30C\uC77C \uC5F4\uB9BC: {file.RelativePath}"
            : $"File opened: {file.RelativePath}";
    }

    private static IEnumerable<SourceFileEntry> BuildSourceTree(string root, string filter)
    {
        var files = EnumerateMatchingFiles(root, filter)
            .OrderBy(path => Normalize(Path.GetRelativePath(root, path)), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var roots = new List<SourceFileEntry>();
        var directories = new Dictionary<string, SourceFileEntry>(StringComparer.OrdinalIgnoreCase);
        var fileCount = 0;

        foreach (var file in files)
        {
            if (fileCount >= MaximumFiles)
            {
                break;
            }

            var relativePath = Normalize(Path.GetRelativePath(root, file));
            var directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? string.Empty;
            var parent = EnsureDirectoryNode(root, directory, roots, directories);

            fileCount++;
            var fileEntry = new SourceFileEntry
            {
                RelativePath = relativePath,
                FullPath = file,
                SizeBytes = new FileInfo(file).Length,
                Depth = relativePath.Count(character => character == '/')
            };
            if (parent == null)
            {
                roots.Add(fileEntry);
            }
            else
            {
                parent.Children.Add(fileEntry);
            }
        }

        return roots;
    }

    private static IEnumerable<string> EnumerateMatchingFiles(string root, string filter)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var count = 0;

        while (pending.Count > 0 && count < MaximumFiles)
        {
            var directory = pending.Pop();
            foreach (var childDirectory in SafeEnumerateDirectories(directory))
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(childDirectory), StringComparer.OrdinalIgnoreCase) &&
                    !IsReparseDirectory(childDirectory) &&
                    WorkspacePathResolver.IsResolvedInsideWorkspace(root, childDirectory))
                {
                    pending.Push(childDirectory);
                }
            }

            foreach (var file in SafeEnumerateFiles(directory))
            {
                if (count >= MaximumFiles)
                {
                    yield break;
                }

                var relativePath = Normalize(Path.GetRelativePath(root, file));
                if (!WorkspacePathResolver.IsResolvedInsideWorkspace(root, file) ||
                    !MatchesFilter(relativePath, filter) ||
                    !IsPreferredSourceFile(file))
                {
                    continue;
                }

                count++;
                yield return file;
            }
        }
    }

    private static SourceFileEntry? EnsureDirectoryNode(
        string root,
        string directory,
        ICollection<SourceFileEntry> roots,
        IDictionary<string, SourceFileEntry> directories)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var parts = directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        SourceFileEntry? parent = null;
        for (var index = 0; index < parts.Length; index++)
        {
            current = string.IsNullOrWhiteSpace(current)
                ? parts[index]
                : $"{current}/{parts[index]}";
            if (directories.TryGetValue(current, out var existing))
            {
                parent = existing;
                continue;
            }

            var entry = new SourceFileEntry
            {
                RelativePath = $"{current}/",
                FullPath = Path.Combine(root, current.Replace('/', Path.DirectorySeparatorChar)),
                IsDirectory = true,
                Depth = index
            };
            directories[current] = entry;
            if (parent == null)
            {
                roots.Add(entry);
            }
            else
            {
                parent.Children.Add(entry);
            }

            parent = entry;
        }

        return parent;
    }

    private static bool MatchesFilter(string relativePath, string filter) =>
        string.IsNullOrWhiteSpace(filter) ||
        relativePath.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static bool IsPreferredSourceFile(string path) =>
        PreferredExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsUnderWorkspace(string workspaceRoot, string path)
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

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path);
        }
        catch
        {
            return [];
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
