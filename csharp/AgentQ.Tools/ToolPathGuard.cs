namespace AgentQ.Tools;

/// <summary>
/// Validates tool paths against the configured workspace root.
/// </summary>
internal static class ToolPathGuard
{
    /// <summary>
    /// Resolves a user-supplied path and verifies that both the lexical and resolved path stay inside the workspace.
    /// </summary>
    public static bool TryResolvePath(string path, out string fullPath, out string? errorMessage)
    {
        fullPath = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Missing required parameter: path";
            return false;
        }

        string workspaceRoot;
        try
        {
            workspaceRoot = GetWorkspaceRoot();
            fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workspaceRoot, path));
        }
        catch (Exception ex) when (IsPathResolutionException(ex))
        {
            errorMessage = $"Path could not be resolved: {path}";
            return false;
        }

        if (!IsWithinRoot(workspaceRoot, fullPath))
        {
            errorMessage = $"Path is outside the workspace root: {path}";
            return false;
        }

        if (!TryEnsureResolvedPathWithinRoot(workspaceRoot, fullPath, out errorMessage))
        {
            errorMessage = $"Path is outside the workspace root after resolving links: {path}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the workspace root used by local tools.
    /// </summary>
    private static string GetWorkspaceRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT")
                             ?? Environment.GetEnvironmentVariable("CLAW_WORKSPACE_ROOT");

        var workspaceRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Environment.CurrentDirectory
            : configuredRoot;

        return Path.GetFullPath(workspaceRoot);
    }

    /// <summary>
    /// Returns true when the candidate path is the root itself or a child of the root.
    /// </summary>
    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedRoot = EnsureTrailingSeparator(rootPath);

        return candidatePath.Equals(rootPath, comparison) ||
               candidatePath.StartsWith(normalizedRoot, comparison);
    }

    private static bool TryEnsureResolvedPathWithinRoot(
        string workspaceRoot,
        string candidatePath,
        out string? errorMessage)
    {
        errorMessage = null;

        if (!TryResolveExistingPath(workspaceRoot, out var resolvedWorkspaceRoot))
        {
            return true;
        }

        if (File.Exists(candidatePath) &&
            TryResolveExistingPath(candidatePath, out var resolvedFile) &&
            !IsWithinRoot(resolvedWorkspaceRoot, resolvedFile))
        {
            return false;
        }

        var directoryToCheck = Directory.Exists(candidatePath)
            ? candidatePath
            : Path.GetDirectoryName(candidatePath);

        while (!string.IsNullOrEmpty(directoryToCheck) &&
               IsWithinRoot(workspaceRoot, directoryToCheck))
        {
            if (Directory.Exists(directoryToCheck) &&
                TryResolveExistingPath(directoryToCheck, out var resolvedDirectory) &&
                !IsWithinRoot(resolvedWorkspaceRoot, resolvedDirectory))
            {
                return false;
            }

            if (PathsEqual(workspaceRoot, directoryToCheck))
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
        catch (Exception ex) when (IsPathResolutionException(ex))
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

    private static bool IsPathResolutionException(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;
    }

    /// <summary>
    /// Ensures a path ends with a directory separator before prefix comparison.
    /// </summary>
    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
