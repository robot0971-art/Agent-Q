namespace AgentQ.Tools;

/// <summary>
/// 도구 경로 보안 검사
/// </summary>
internal static class ToolPathGuard
{
    /// <summary>
    /// 경로 확인 및 해석
    /// </summary>
    /// <param name="path">입력 경로</param>
    /// <param name="fullPath">전체 경로 (out)</param>
    /// <param name="errorMessage">오류 메시지 (out)</param>
    /// <returns>검사 통과 여부</returns>
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

        // Resolve path relative to the workspace root if it's not absolute
        fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workspaceRoot, path));

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
    /// 작업 공간 루트 경로 가져오기
    /// </summary>
    /// <returns>작업 공간 루트 경로</returns>
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
    /// 경로가 루트 내에 있는지 확인
    /// </summary>
    /// <param name="rootPath">루트 경로</param>
    /// <param name="candidatePath">검사할 경로</param>
    /// <returns>루트 내 포함 여부</returns>
    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedRoot = EnsureTrailingSeparator(rootPath);
        // candidatePath is already absolute and normalized via Path.GetFullPath in TryResolvePath

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

    /// <summary>
    /// 경로 끝에 구분자 추가
    /// </summary>
    /// <param name="path">원본 경로</param>
    /// <returns>구분자가 추가된 경로</returns>
    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}

