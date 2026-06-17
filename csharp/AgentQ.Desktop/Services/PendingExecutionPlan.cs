using System.IO;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public sealed record PendingExecutionPlan
{
    public required string Id { get; init; }

    public required string WorkspaceRoot { get; init; }

    public required string Goal { get; init; }

    public required string SourceAssistantText { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public int RemainingUserTurns { get; init; } = 1;

    public bool IsExpired(DateTimeOffset now) =>
        RemainingUserTurns <= 0 || now - CreatedAtUtc > TimeSpan.FromMinutes(30);
}

public sealed record PendingPlanResolution(
    bool Resolved,
    bool ClearPendingPlan,
    string RoutingText,
    string Reason);

public static partial class PendingPlanResolver
{
    public static bool TryCapture(
        string assistantText,
        AgentTurnState turnState,
        DateTimeOffset now,
        out PendingExecutionPlan plan)
    {
        plan = default!;
        if (turnState.EffectiveIntent.Type != TurnIntentType.Conversation ||
            turnState.TaskContract.IsActionable ||
            string.IsNullOrWhiteSpace(assistantText))
        {
            return false;
        }

        var normalized = Normalize(assistantText);
        if (!ContainsProceedInstruction(normalized) ||
            !ContainsExecutionPromise(normalized) ||
            !LooksLikeActionablePlan(normalized))
        {
            return false;
        }

        var goal = ExtractGoal(assistantText, turnState.RoutingText);
        if (string.IsNullOrWhiteSpace(goal) || goal.Length < 8)
        {
            return false;
        }

        plan = new PendingExecutionPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            WorkspaceRoot = turnState.WorkspaceRoot,
            Goal = goal,
            SourceAssistantText = DesktopPromptBuilder.Truncate(assistantText.Trim(), 4000),
            CreatedAtUtc = now,
            RemainingUserTurns = 1
        };
        return true;
    }

    public static PendingPlanResolution Resolve(
        string userText,
        PendingExecutionPlan? pendingPlan,
        string workspaceRoot,
        DateTimeOffset now)
    {
        if (pendingPlan == null)
        {
            return new PendingPlanResolution(false, false, userText, "No pending execution plan.");
        }

        if (pendingPlan.IsExpired(now))
        {
            return new PendingPlanResolution(false, true, userText, "Pending execution plan expired.");
        }

        if (!string.Equals(
                Path.GetFullPath(pendingPlan.WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return new PendingPlanResolution(false, true, userText, "Workspace changed, so the pending execution plan was cleared.");
        }

        var normalized = Normalize(userText);
        if (!IsProceedApproval(normalized))
        {
            return new PendingPlanResolution(false, ShouldClearForTopicChange(normalized), userText, "The user did not approve the pending plan.");
        }

        if (LooksLikeDifferentTopic(normalized))
        {
            return new PendingPlanResolution(false, true, userText, "The user introduced a different topic instead of approving the pending plan.");
        }

        var routingText =
            $"{pendingPlan.Goal.Trim()}{Environment.NewLine}{Environment.NewLine}" +
            $"The user approved the immediately previous execution plan with: {userText.Trim()}";
        return new PendingPlanResolution(true, true, routingText, "User approved the immediately previous pending execution plan.");
    }

    private static string ExtractGoal(string assistantText, string fallback)
    {
        var match = PlanTitleRegex().Match(assistantText);
        if (match.Success)
        {
            return match.Groups["goal"].Value.Trim();
        }

        var lines = assistantText
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim(' ', '#', '*', '-', ':'))
            .Where(line => line.Length >= 8)
            .ToList();
        var candidate = lines.FirstOrDefault(line =>
            ContainsAny(Normalize(line), "구현계획", "implementationplan", "프로젝트", "project", "로그인", "회원가입", "사이트", "app"));
        return string.IsNullOrWhiteSpace(candidate) ? fallback.Trim() : candidate.Trim();
    }

    private static bool ContainsProceedInstruction(string normalized) =>
        ContainsAny(
            normalized,
            "진행해줘",
            "이대로진행",
            "만들어줘라고",
            "진행하겠습니다",
            "바로실행",
            "바로구현",
            "goahead",
            "proceed",
            "saygoahead",
            "tellmetoproceed");

    private static bool ContainsExecutionPromise(string normalized) =>
        ContainsAny(
            normalized,
            "바로프로젝트를생성",
            "바로생성",
            "바로구현",
            "생성하고구현",
            "만들겠습니다",
            "구현하겠습니다",
            "willcreate",
            "willbuild",
            "iwillimplement",
            "icancreate");

    private static bool LooksLikeActionablePlan(string normalized) =>
        ContainsAny(
            normalized,
            "구현계획",
            "파일구조",
            "기술스택",
            "주요기능",
            "검증",
            "implementationplan",
            "filestructure",
            "techstack");

    private static bool IsProceedApproval(string normalized)
    {
        if (normalized.Length > 80)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "진행해줘",
            "이대로진행",
            "그대로진행",
            "좋아진행",
            "좋아해줘",
            "만들어줘",
            "구현해줘",
            "해줘",
            "goahead",
            "proceed",
            "continue",
            "doit",
            "yes");
    }

    private static bool LooksLikeDifferentTopic(string normalized) =>
        ContainsAny(
            normalized,
            "말고",
            "다른",
            "취소",
            "멈춰",
            "하지마",
            "테스트결과",
            "오류",
            "설명",
            "왜",
            "notthat",
            "instead",
            "cancel",
            "stop");

    private static bool ShouldClearForTopicChange(string normalized) =>
        normalized.Length > 0 && !IsProceedApproval(normalized);

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string text) =>
        new string(text.Where(ch => !char.IsWhiteSpace(ch) && ch != '"' && ch != '\'' && ch != '`').ToArray()).ToLowerInvariant();

    [GeneratedRegex(@"(?:구현\s*계획|implementation\s*plan)\s*[:：-]?\s*(?<goal>.+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlanTitleRegex();
}
