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

