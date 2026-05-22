namespace AgentQ.Desktop.Services;

public static class DesktopTaskClassifier
{
    public static DesktopTaskKind Classify(string userText)
    {
        var text = userText.ToLowerInvariant();

        if (ContainsAny(text, "review", "code review", "리뷰", "검토"))
        {
            return DesktopTaskKind.CodeReview;
        }

        if (ContainsAny(text, "readme", "docs", "document", "문서", "릴리즈 노트", "설명"))
        {
            return DesktopTaskKind.Documentation;
        }

        if (ContainsAny(text, "verify", "검증", "테스트", "빌드 실패", "컴파일"))
        {
            return DesktopTaskKind.VerificationFailure;
        }

        if (ContainsAny(text, "test failed", "build failed", "verification failed", "실패", "에러", "오류", "안됨", "안 돼", "고쳐", "fix", "bug", "버그"))
        {
            return DesktopTaskKind.BugFix;
        }

        if (ContainsAny(text, "refactor", "리팩터", "리팩토", "정리", "구조 개선"))
        {
            return DesktopTaskKind.Refactor;
        }

        if (ContainsAny(text, "analyze", "분석", "파악", "찾아", "어디", "확인", "조사"))
        {
            return DesktopTaskKind.Analysis;
        }

        if (ContainsAny(text, "add", "implement", "create", "만들", "추가", "구현", "넣자", "진행"))
        {
            return DesktopTaskKind.Feature;
        }

        return DesktopTaskKind.General;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
