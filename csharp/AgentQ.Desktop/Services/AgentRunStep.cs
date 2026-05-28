namespace AgentQ.Desktop.Services;

public sealed class AgentRunStep
{
    public required AgentRunState State { get; init; }

    public required string Title { get; init; }

    public string Detail { get; init; } = string.Empty;

    public bool UseKoreanUi { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public string CreatedAtText => CreatedAt.ToString("HH:mm:ss");

    public string StateText => UseKoreanUi ? LocalizeState(State) : State.ToString();

    public string TimelineLabel => UseKoreanUi ? State switch
    {
        AgentRunState.Planning => "계획",
        AgentRunState.GatheringContext => "컨텍스트",
        AgentRunState.Generating => "모델",
        AgentRunState.RunningTool => "도구",
        AgentRunState.WaitingForApproval => "승인",
        AgentRunState.RecordingChanges => "변경",
        AgentRunState.Verifying => "검증",
        AgentRunState.Done => "완료",
        AgentRunState.Failed => "실패",
        AgentRunState.Cancelled => "취소",
        _ => "실행"
    } : State switch
    {
        AgentRunState.Planning => "PLAN",
        AgentRunState.GatheringContext => "CONTEXT",
        AgentRunState.Generating => "MODEL",
        AgentRunState.RunningTool => "TOOL",
        AgentRunState.WaitingForApproval => "APPROVAL",
        AgentRunState.RecordingChanges => "CHANGE",
        AgentRunState.Verifying => "VERIFY",
        AgentRunState.Done => "DONE",
        AgentRunState.Failed => "FAILED",
        AgentRunState.Cancelled => "CANCELLED",
        _ => "RUN"
    };

    public string DisplayTitle => UseKoreanUi ? LocalizeTitle(Title) : Title;

    public string AccentBrush => State switch
    {
        AgentRunState.Done => "#37D67A",
        AgentRunState.Failed => "#F87171",
        AgentRunState.Cancelled => "#FBBF24",
        AgentRunState.WaitingForApproval => "#FBBF24",
        AgentRunState.Verifying => "#5BA7FF",
        AgentRunState.RunningTool => "#A78BFA",
        AgentRunState.RecordingChanges => "#34D399",
        AgentRunState.Generating => "#60A5FA",
        AgentRunState.GatheringContext => "#93C5FD",
        AgentRunState.Planning => "#38BDF8",
        _ => "#B7C4D1"
    };

    public string BadgeBackground => State switch
    {
        AgentRunState.Done => "#062B1A",
        AgentRunState.Failed => "#3A1111",
        AgentRunState.Cancelled => "#331D03",
        AgentRunState.WaitingForApproval => "#331D03",
        AgentRunState.Verifying => "#0B2A4A",
        AgentRunState.RunningTool => "#251A44",
        AgentRunState.RecordingChanges => "#062B1A",
        _ => "#13202D"
    };

    public string TimelineDetail => string.IsNullOrWhiteSpace(Detail)
        ? UseKoreanUi ? "추가 세부 정보 없음." : "No additional detail."
        : Detail;

    private static string LocalizeState(AgentRunState state)
    {
        return state switch
        {
            AgentRunState.Planning => "계획",
            AgentRunState.GatheringContext => "컨텍스트 수집",
            AgentRunState.Generating => "응답 생성",
            AgentRunState.RunningTool => "도구 실행",
            AgentRunState.WaitingForApproval => "승인 대기",
            AgentRunState.RecordingChanges => "변경 기록",
            AgentRunState.Verifying => "검증",
            AgentRunState.Done => "완료",
            AgentRunState.Failed => "실패",
            AgentRunState.Cancelled => "취소",
            _ => state.ToString()
        };
    }

    public static string LocalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        if (title.StartsWith("Permission: ", StringComparison.OrdinalIgnoreCase))
        {
            return title.Replace("Permission:", "권한:", StringComparison.OrdinalIgnoreCase)
                .Replace("Allowed by run approval", "실행 권한으로 허용", StringComparison.OrdinalIgnoreCase)
                .Replace("Allowed by policy", "정책상 허용", StringComparison.OrdinalIgnoreCase)
                .Replace("Approved", "승인됨", StringComparison.OrdinalIgnoreCase)
                .Replace("Denied", "거부됨", StringComparison.OrdinalIgnoreCase)
                .Replace("Blocked", "차단됨", StringComparison.OrdinalIgnoreCase);
        }

        if (title.StartsWith("Blocked:", StringComparison.OrdinalIgnoreCase))
        {
            return title.Replace("Blocked:", "차단됨:", StringComparison.OrdinalIgnoreCase);
        }

        if (title.StartsWith("Evidence:", StringComparison.OrdinalIgnoreCase))
        {
            return title.Replace("Evidence:", "근거:", StringComparison.OrdinalIgnoreCase);
        }

        return title switch
        {
            "Waiting for approval" => "승인 대기",
            "Running verification" => "검증 실행 중",
            "Verification passed" => "검증 통과",
            "Verification cancelled" => "검증 취소됨",
            "Run complete" => "실행 완료",
            "Run started" => "실행 시작",
            _ => title
        };
    }
}
