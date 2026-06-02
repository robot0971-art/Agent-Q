namespace AgentQ.Desktop.Services;

public sealed class AgentRunStep
{
    public required AgentRunState State { get; init; }

    public required string Title { get; init; }

    public string Detail { get; init; } = string.Empty;

    public bool UseKoreanUi { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public string CreatedAtText => CreatedAt.ToString("HH:mm:ss");

    public string StateText => DesktopLocalizer.RunState(State, UseKoreanUi);

    public string TimelineLabel => DesktopLocalizer.TimelineLabel(State, UseKoreanUi);

    public string DisplayTitle => DesktopLocalizer.TimelineTitle(Title, UseKoreanUi);

    public string AccentBrush => State switch
    {
        AgentRunState.Done => "#37D67A",
        AgentRunState.Failed => "#F87171",
        AgentRunState.Cancelled => "#FBBF24",
        AgentRunState.Clarifying => "#FBBF24",
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
        AgentRunState.Clarifying => "#331D03",
        AgentRunState.WaitingForApproval => "#331D03",
        AgentRunState.Verifying => "#0B2A4A",
        AgentRunState.RunningTool => "#251A44",
        AgentRunState.RecordingChanges => "#062B1A",
        _ => "#13202D"
    };

    public string TimelineDetail => string.IsNullOrWhiteSpace(Detail)
        ? DesktopLocalizer.NoTimelineDetail(UseKoreanUi)
        : Detail;
}
