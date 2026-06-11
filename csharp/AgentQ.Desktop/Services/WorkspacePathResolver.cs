using System.IO;

namespace AgentQ.Desktop.Services;

internal static class WorkspacePathResolver
{
    public static bool IsInsideWorkspace(string workspaceRoot, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var root = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(fullPath);
        var rootWithSeparator = root + Path.DirectorySeparatorChar;

        return candidate.Equals(root, comparison) ||
               candidate.StartsWith(rootWithSeparator, comparison);
    }

    public static bool IsResolvedInsideWorkspace(string workspaceRoot, string fullPath)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var candidate = Path.GetFullPath(fullPath);
        if (!IsInsideWorkspace(root, candidate))
        {
            return false;
        }

        if (!TryResolveExistingPath(root, out var resolvedRoot))
        {
            return true;
        }

        if (File.Exists(candidate) &&
            TryResolveExistingPath(candidate, out var resolvedFile) &&
            !IsInsideWorkspace(resolvedRoot, resolvedFile))
        {
            return false;
        }

        var directoryToCheck = Directory.Exists(candidate)
            ? candidate
            : Path.GetDirectoryName(candidate);
        while (!string.IsNullOrWhiteSpace(directoryToCheck) &&
               IsInsideWorkspace(root, directoryToCheck))
        {
            if (Directory.Exists(directoryToCheck) &&
                TryResolveExistingPath(directoryToCheck, out var resolvedDirectory) &&
                !IsInsideWorkspace(resolvedRoot, resolvedDirectory))
            {
                return false;
            }

            if (PathsEqual(root, directoryToCheck))
            {
                break;
            }

            directoryToCheck = Path.GetDirectoryName(directoryToCheck);
        }

        return true;
    }

    private static bool TryResolveExistingPath(string path, out string resolvedPath)
    {
        resolvedPath = Path.GetFullPath(path);
        try
        {
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                var target = directory.ResolveLinkTarget(returnFinalTarget: true);
                resolvedPath = Path.GetFullPath(target?.FullName ?? directory.FullName);
                return true;
            }

            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                var target = file.ResolveLinkTarget(returnFinalTarget: true);
                resolvedPath = Path.GetFullPath(target?.FullName ?? file.FullName);
                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            comparison);
    }
}
