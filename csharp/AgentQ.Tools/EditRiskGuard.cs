namespace AgentQ.Tools;

internal sealed record EditRiskAssessment(
    bool IsHighRisk,
    string Reason,
    int LineCount,
    bool LooksLikeUnityBehaviour);

internal static class EditRiskGuard
{
    private const int LargeFileLineThreshold = 500;
    private const double LargeReplacementRatio = 0.35;

    private static readonly string[] CoreNameMarkers =
    [
        "controller",
        "service",
        "manager",
        "registry",
        "runner",
        "workflow"
    ];

    public static EditRiskAssessment AssessExistingFile(string fullPath, string content)
    {
        var lineCount = CountLines(content);
        var fileName = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath);
        var looksLikeUnityBehaviour = extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) &&
                                      (content.Contains(": MonoBehaviour", StringComparison.Ordinal) ||
                                       content.Contains("[SerializeField]", StringComparison.Ordinal));
        var isUnityAsset = extension.Equals(".unity", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".asset", StringComparison.OrdinalIgnoreCase);
        var isLarge = lineCount >= LargeFileLineThreshold;
        var isCoreNamed = CoreNameMarkers.Any(marker => fileName.Contains(marker, StringComparison.OrdinalIgnoreCase));

        if (looksLikeUnityBehaviour)
        {
            return new EditRiskAssessment(true, "Unity MonoBehaviour or SerializeField bindings detected", lineCount, true);
        }

        if (isUnityAsset)
        {
            return new EditRiskAssessment(true, "Unity serialized asset file detected", lineCount, false);
        }

        if (isLarge && isCoreNamed)
        {
            return new EditRiskAssessment(true, $"large core file detected ({lineCount} lines)", lineCount, false);
        }

        if (isLarge)
        {
            return new EditRiskAssessment(true, $"large file detected ({lineCount} lines)", lineCount, false);
        }

        return new EditRiskAssessment(false, "standard file", lineCount, looksLikeUnityBehaviour);
    }

    public static bool IsRiskAcknowledged(Dictionary<string, object?> input)
    {
        return TryGetBoolean(input, "allow_high_risk_edit", out var allowed) && allowed;
    }

    public static string BuildWriteBlockMessage(string path, EditRiskAssessment assessment)
    {
        return "Refusing high-risk whole-file rewrite. " +
               $"Target: {path}. Reason: {assessment.Reason}. " +
               "Use edit_file with a minimal old_string/new_string patch, preserve SerializeField names and Inspector bindings, " +
               "and compile after each phase. Set allow_high_risk_edit=true only after explicit user approval.";
    }

    public static string? ValidateReplacement(
        string path,
        string content,
        string oldString,
        bool replaceAll,
        Dictionary<string, object?> input)
    {
        var assessment = AssessExistingFile(path, content);
        if (!assessment.IsHighRisk || IsRiskAcknowledged(input))
        {
            return null;
        }

        var replacementRatio = content.Length == 0 ? 0 : (double)oldString.Length / content.Length;
        if (replaceAll || replacementRatio >= LargeReplacementRatio)
        {
            return "Refusing high-risk broad edit. " +
                   $"Target: {path}. Reason: {assessment.Reason}. " +
                   "Use a smaller patch-sized replacement, avoid replace_all on high-risk files, preserve SerializeField names and Inspector bindings, " +
                   "and compile after each phase. Set allow_high_risk_edit=true only after explicit user approval.";
        }

        return null;
    }

    private static int CountLines(string content)
    {
        if (content.Length == 0)
        {
            return 0;
        }

        var count = 1;
        foreach (var character in content)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryGetBoolean(Dictionary<string, object?> input, string key, out bool value)
    {
        value = false;
        if (!input.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        if (rawValue is string stringValue && bool.TryParse(stringValue, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
