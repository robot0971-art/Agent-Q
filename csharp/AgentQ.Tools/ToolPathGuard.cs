namespace AgentQ.Tools;

internal static class ToolPathGuard
{
    public static bool TryResolvePath(string path, out string fullPath, out string? errorMessage)
    {
        fullPath = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Missing required parameter: path";
            return false;
        }

        var workspaceRoot = GetWorkspaceRoot();
        fullPath = Path.GetFullPath(path);

        if (!IsWithinRoot(workspaceRoot, fullPath))
        {
            errorMessage = $"Path is outside the workspace root: {path}";
            return false;
        }

        return true;
    }

    private static string GetWorkspaceRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("CLAW_WORKSPACE_ROOT");
        var workspaceRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Environment.CurrentDirectory
            : configuredRoot;

        return Path.GetFullPath(workspaceRoot);
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedRoot = EnsureTrailingSeparator(rootPath);
        var normalizedCandidate = Path.GetFullPath(candidatePath);

        return normalizedCandidate.Equals(rootPath, comparison) ||
               normalizedCandidate.StartsWith(normalizedRoot, comparison);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}

