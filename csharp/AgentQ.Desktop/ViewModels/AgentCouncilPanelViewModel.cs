using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class AgentCouncilPanelViewModel : INotifyPropertyChanged
{
    private const int MaxEvents = 80;
    private bool _useKoreanUi;
    private string _currentTopic = "No active meeting.";
    private string _phaseText = "Idle";
    private string _activeSpeakerText = "None";
    private string _summaryText = "Agent roles will appear when a request starts.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AgentCouncilParticipantViewModel> Participants { get; } = [];

    public ObservableCollection<AgentCouncilEventViewModel> Events { get; } = [];

    public bool UseKoreanUi
    {
        get => _useKoreanUi;
        set
        {
            if (!SetField(ref _useKoreanUi, value))
            {
                return;
            }

            NotifyTextChanged();
            if (Participants.Count == 0 && Events.Count == 0)
            {
                Reset(value);
            }
        }
    }

    public string HeaderText => UseKoreanUi ? "\uC5D0\uC774\uC804\uD2B8 \uD68C\uC758\uC7A5" : "Agent Council";

    public string HelpText => UseKoreanUi
        ? "\uC791\uC5C5\uC744 \uC5ED\uD560\uB85C \uB098\uB204\uACE0, \uAC01 \uC5D0\uC774\uC804\uD2B8\uAC00 \uC5B4\uB5A4 \uC21C\uC11C\uB85C \uBC1C\uC5B8\uD558\uB294\uC9C0 \uC2E4\uD589 \uD750\uB984\uC5D0 \uB9DE\uCDB0 \uBCF4\uC5EC\uC90D\uB2C8\uB2E4."
        : "Shows how AgentQ splits the task into roles and which agent is speaking as the run unfolds.";

    public string ParticipantsHeaderText => UseKoreanUi ? "\uCC38\uC11D \uC5D0\uC774\uC804\uD2B8" : "Participants";

    public string TranscriptHeaderText => UseKoreanUi ? "\uD68C\uC758 \uAE30\uB85D" : "Meeting transcript";

    public string TopicLabelText => UseKoreanUi ? "\uC758\uC81C" : "Topic";

    public string PhaseLabelText => UseKoreanUi ? "\uB2E8\uACC4" : "Phase";

    public string ActiveSpeakerLabelText => UseKoreanUi ? "\uD604\uC7AC \uBC1C\uC5B8" : "Speaking";

    public string CurrentTopic
    {
        get => _currentTopic;
        private set => SetField(ref _currentTopic, value);
    }

    public string PhaseText
    {
        get => _phaseText;
        private set => SetField(ref _phaseText, value);
    }

    public string ActiveSpeakerText
    {
        get => _activeSpeakerText;
        private set => SetField(ref _activeSpeakerText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetField(ref _summaryText, value);
    }

    public void Reset(bool useKoreanUi)
    {
        _useKoreanUi = useKoreanUi;
        Participants.Clear();
        Events.Clear();
        CurrentTopic = UseKoreanUi ? "\uC9C4\uD589 \uC911\uC778 \uD68C\uC758\uAC00 \uC5C6\uC2B5\uB2C8\uB2E4." : "No active meeting.";
        PhaseText = UseKoreanUi ? "\uB300\uAE30" : "Idle";
        ActiveSpeakerText = UseKoreanUi ? "\uC5C6\uC74C" : "None";
        SummaryText = UseKoreanUi
            ? "\uC694\uCCAD\uC774 \uC2DC\uC791\uB418\uBA74 \uC5D0\uC774\uC804\uD2B8 \uC5ED\uD560\uC774 \uC5EC\uAE30\uC5D0 \uD45C\uC2DC\uB429\uB2C8\uB2E4."
            : "Agent roles will appear when a request starts.";
        NotifyTextChanged();
    }

    public void StartSession(string prompt, DesktopTaskProfile profile, MultiAgentRolePlan rolePlan, bool useKoreanUi)
    {
        _useKoreanUi = useKoreanUi;
        Participants.Clear();
        Events.Clear();

        CurrentTopic = Trim(prompt, 130);
        PhaseText = FormatKind(profile.Kind);
        ActiveSpeakerText = RoleDisplayName("Coordinator");
        SummaryText = UseKoreanUi
            ? $"{FormatKind(profile.Kind)} \uC791\uC5C5\uC73C\uB85C \uBD84\uB958\uB418\uC5C8\uACE0 {rolePlan.Steps.Count:0}\uAC1C \uC5ED\uD560\uC774 \uD68C\uC758\uC5D0 \uCC38\uC11D\uD569\uB2C8\uB2E4."
            : $"Classified as {FormatKind(profile.Kind)} with {rolePlan.Steps.Count:0} role(s) at the table.";

        Participants.Add(new AgentCouncilParticipantViewModel(
            "Coordinator",
            RoleDisplayName("Coordinator"),
            UseKoreanUi ? "\uC694\uCCAD\uC744 \uBC1B\uACE0 \uC5ED\uD560 \uD750\uB984\uC744 \uC870\uC728" : "Receives the request and coordinates role flow",
            UseKoreanUi ? "\uC9C4\uD589 \uC911" : "Opening",
            "#5BA7FF",
            "#0B2A4A",
            false));

        foreach (var step in rolePlan.Steps)
        {
            var roleKey = step.Role.ToString();
            Participants.Add(new AgentCouncilParticipantViewModel(
                roleKey,
                RoleDisplayName(roleKey),
                FormatResponsibility(step.Responsibility),
                step.IsParallelCandidate
                    ? (UseKoreanUi ? "\uBCD1\uB82C \uD6C4\uBCF4" : "Parallel candidate")
                    : (UseKoreanUi ? "\uB300\uAE30" : "Queued"),
                RoleAccent(roleKey),
                RoleBackground(roleKey),
                step.IsParallelCandidate));
        }

        AddEvent(
            "Coordinator",
            UseKoreanUi ? "\uD68C\uC758 \uC2DC\uC791" : "Meeting opened",
            SummaryText,
            AgentRunState.Planning);
        NotifyTextChanged();
    }

    public void RecordRunStep(AgentRunStep step)
    {
        var roleKey = InferRole(step.Title, step.Detail, step.State);
        var status = step.State switch
        {
            AgentRunState.Done => UseKoreanUi ? "\uC644\uB8CC" : "Done",
            AgentRunState.Failed => UseKoreanUi ? "\uD655\uC778 \uD544\uC694" : "Needs attention",
            AgentRunState.Cancelled => UseKoreanUi ? "\uCDE8\uC18C" : "Cancelled",
            AgentRunState.WaitingForApproval => UseKoreanUi ? "\uC2B9\uC778 \uB300\uAE30" : "Waiting",
            AgentRunState.RunningTool => UseKoreanUi ? "\uB3C4\uAD6C \uC0AC\uC6A9" : "Using tool",
            AgentRunState.Verifying => UseKoreanUi ? "\uAC80\uC99D \uC911" : "Verifying",
            _ => UseKoreanUi ? "\uBC1C\uC5B8 \uC911" : "Speaking"
        };

        EnsureParticipant(roleKey).StatusText = status;
        ActiveSpeakerText = RoleDisplayName(roleKey);
        PhaseText = DesktopLocalizer.RunState(step.State, UseKoreanUi);
        AddEvent(roleKey, step.DisplayTitle, step.TimelineDetail, step.State);
    }

    public void RecordToolStarted(string toolName)
    {
        var roleKey = InferToolRole(toolName);
        EnsureParticipant(roleKey).StatusText = UseKoreanUi ? "\uB3C4\uAD6C \uC0AC\uC6A9" : "Using tool";
        ActiveSpeakerText = RoleDisplayName(roleKey);
        AddEvent(
            roleKey,
            UseKoreanUi ? "\uB3C4\uAD6C \uC2E4\uD589" : "Tool started",
            toolName,
            AgentRunState.RunningTool);
    }

    public void RecordToolCompleted(string toolName, int outputLength)
    {
        var roleKey = InferToolRole(toolName);
        EnsureParticipant(roleKey).StatusText = UseKoreanUi ? "\uB3C4\uAD6C \uC644\uB8CC" : "Tool complete";
        AddEvent(
            roleKey,
            UseKoreanUi ? "\uB3C4\uAD6C \uC644\uB8CC" : "Tool completed",
            UseKoreanUi ? $"{toolName} ({outputLength:0}\uC790)" : $"{toolName} ({outputLength:0} chars)",
            AgentRunState.Done);
    }

    public void RecordToolError(string toolName, string error)
    {
        var roleKey = InferToolRole(toolName);
        EnsureParticipant(roleKey).StatusText = UseKoreanUi ? "\uC624\uB958" : "Error";
        AddEvent(roleKey, UseKoreanUi ? "\uB3C4\uAD6C \uC624\uB958" : "Tool error", $"{toolName}: {Trim(error, 180)}", AgentRunState.Failed);
    }

    public void RecordFileChanged(FileChangeRecord change)
    {
        var roleKey = "Coder";
        EnsureParticipant(roleKey).StatusText = UseKoreanUi ? "\uBCC0\uACBD \uAE30\uB85D" : "Recorded change";
        AddEvent(roleKey, UseKoreanUi ? "\uD30C\uC77C \uBCC0\uACBD" : "File changed", $"{change.RelativePath} {change.Summary}", AgentRunState.RecordingChanges);
    }

    public void RecordVerificationPlan(AgentVerificationPlan plan)
    {
        var roleKey = "Tester";
        EnsureParticipant(roleKey).StatusText = plan.AlreadySatisfied
            ? (UseKoreanUi ? "\uC774\uBBF8 \uCDA9\uC871" : "Already satisfied")
            : (UseKoreanUi ? "\uAC80\uC99D \uC900\uBE44" : "Preparing check");
        AddEvent(roleKey, plan.Title, plan.Detail, plan.AlreadySatisfied ? AgentRunState.Done : AgentRunState.Planning);
    }

    public void RecordVerificationResult(VerificationResultCard result)
    {
        var roleKey = "Tester";
        var passed = result.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase);
        EnsureParticipant(roleKey).StatusText = passed
            ? (UseKoreanUi ? "\uD1B5\uACFC" : "Passed")
            : (UseKoreanUi ? "\uC2E4\uD328" : "Failed");
        AddEvent(roleKey, result.Title, result.Summary, passed ? AgentRunState.Done : AgentRunState.Failed);
    }

    private AgentCouncilParticipantViewModel EnsureParticipant(string roleKey)
    {
        var participant = Participants.FirstOrDefault(agent => agent.RoleKey.Equals(roleKey, StringComparison.OrdinalIgnoreCase));
        if (participant != null)
        {
            return participant;
        }

        participant = new AgentCouncilParticipantViewModel(
            roleKey,
            RoleDisplayName(roleKey),
            UseKoreanUi ? "\uC2E4\uD589 \uD750\uB984\uC5D0\uC11C \uAC10\uC9C0\uB41C \uC5ED\uD560" : "Detected from the current run",
            UseKoreanUi ? "\uD65C\uC131" : "Active",
            RoleAccent(roleKey),
            RoleBackground(roleKey),
            false);
        Participants.Add(participant);
        return participant;
    }

    private void AddEvent(string roleKey, string title, string detail, AgentRunState state)
    {
        Events.Insert(0, new AgentCouncilEventViewModel(
            RoleDisplayName(roleKey),
            title,
            detail,
            DesktopLocalizer.TimelineLabel(state, UseKoreanUi),
            RoleAccent(roleKey),
            RoleBackground(roleKey)));

        while (Events.Count > MaxEvents)
        {
            Events.RemoveAt(Events.Count - 1);
        }
    }

    private string InferRole(string title, string detail, AgentRunState state)
    {
        var text = $"{title} {detail}";
        if (ContainsAny(text, "tester", "verify", "verification", "test"))
        {
            return "Tester";
        }

        if (ContainsAny(text, "reviewer", "review"))
        {
            return "Reviewer";
        }

        if (ContainsAny(text, "coder", "file changed", "edit", "write", "scaffold"))
        {
            return "Coder";
        }

        if (ContainsAny(text, "planner", "plan", "task profile", "multi-agent roles", "decomposing") ||
            state == AgentRunState.Planning ||
            state == AgentRunState.GatheringContext)
        {
            return "Planner";
        }

        return "Coordinator";
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string InferToolRole(string toolName)
    {
        if (ContainsAny(toolName, "write", "edit", "bash", "run_command", "shell"))
        {
            return "Coder";
        }

        if (ContainsAny(toolName, "test", "verify"))
        {
            return "Tester";
        }

        return "Planner";
    }

    private string RoleDisplayName(string roleKey) => roleKey switch
    {
        "Coordinator" => UseKoreanUi ? "\uC870\uC728\uC790" : "Coordinator",
        "Planner" => UseKoreanUi ? "\uACC4\uD68D\uC790" : "Planner",
        "Coder" => UseKoreanUi ? "\uAD6C\uD604\uC790" : "Coder",
        "Reviewer" => UseKoreanUi ? "\uB9AC\uBDF0\uC5B4" : "Reviewer",
        "Tester" => UseKoreanUi ? "\uD14C\uC2A4\uD130" : "Tester",
        _ => roleKey
    };

    private string FormatResponsibility(string responsibility)
    {
        if (!UseKoreanUi)
        {
            return responsibility;
        }

        return responsibility
            .Replace("map existing patterns, contracts, and implementation slices", "\uAE30\uC874 \uAD6C\uC870, \uACC4\uC57D, \uAD6C\uD604 \uB2E8\uC704 \uD30C\uC545", StringComparison.OrdinalIgnoreCase)
            .Replace("implement one cohesive slice without unrelated refactors", "\uAD00\uB828 \uBC94\uC704\uB9CC \uC9D1\uC911 \uAD6C\uD604", StringComparison.OrdinalIgnoreCase)
            .Replace("review touched behavior, compatibility, and edge cases", "\uBCC0\uACBD \uB3D9\uC791, \uD638\uD658\uC131, \uC5E3\uC9C0\uCF00\uC774\uC2A4 \uAC80\uD1A0", StringComparison.OrdinalIgnoreCase)
            .Replace("select and run relevant build or test checks", "\uAD00\uB828 \uBE4C\uB4DC/\uD14C\uC2A4\uD2B8 \uC120\uD0DD \uBC0F \uC2E4\uD589", StringComparison.OrdinalIgnoreCase)
            .Replace("classify the failure and choose the smallest repair surface", "\uC2E4\uD328\uB97C \uBD84\uB958\uD558\uACE0 \uCD5C\uC18C \uC218\uC815 \uBC94\uC704 \uC120\uD0DD", StringComparison.OrdinalIgnoreCase)
            .Replace("make the minimal root-cause change", "\uADFC\uBCF8 \uC6D0\uC778\uC5D0 \uB300\uD55C \uCD5C\uC18C \uC218\uC815", StringComparison.OrdinalIgnoreCase)
            .Replace("rerun focused verification and summarize remaining risk", "\uC9D1\uC911 \uAC80\uC99D \uC7AC\uC2E4\uD589 \uBC0F \uC794\uC5EC \uC704\uD5D8 \uC694\uC57D", StringComparison.OrdinalIgnoreCase);
    }

    private string FormatKind(DesktopTaskKind kind)
    {
        if (!UseKoreanUi)
        {
            return kind.ToString();
        }

        return kind switch
        {
            DesktopTaskKind.Feature => "\uAE30\uB2A5 \uAD6C\uD604",
            DesktopTaskKind.BugFix => "\uBC84\uADF8 \uC218\uC815",
            DesktopTaskKind.VerificationFailure => "\uAC80\uC99D \uC2E4\uD328",
            DesktopTaskKind.CodeReview => "\uCF54\uB4DC \uB9AC\uBDF0",
            DesktopTaskKind.Documentation => "\uBB38\uC11C\uD654",
            DesktopTaskKind.Analysis => "\uBD84\uC11D",
            DesktopTaskKind.Refactor => "\uB9AC\uD329\uD130",
            _ => kind.ToString()
        };
    }

    private static string RoleAccent(string roleKey) => roleKey switch
    {
        "Coordinator" => "#5BA7FF",
        "Planner" => "#38BDF8",
        "Coder" => "#34D399",
        "Reviewer" => "#FBBF24",
        "Tester" => "#A78BFA",
        _ => "#B7C4D1"
    };

    private static string RoleBackground(string roleKey) => roleKey switch
    {
        "Coordinator" => "#0B2A4A",
        "Planner" => "#082A38",
        "Coder" => "#062B1A",
        "Reviewer" => "#331D03",
        "Tester" => "#251A44",
        _ => "#13202D"
    };

    private static string Trim(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= max ? value : value[..max] + "...";
    }

    private void NotifyTextChanged()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(HeaderText),
                     nameof(HelpText),
                     nameof(ParticipantsHeaderText),
                     nameof(TranscriptHeaderText),
                     nameof(TopicLabelText),
                     nameof(PhaseLabelText),
                     nameof(ActiveSpeakerLabelText)
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class AgentCouncilParticipantViewModel : INotifyPropertyChanged
{
    private string _statusText;

    public AgentCouncilParticipantViewModel(
        string roleKey,
        string displayName,
        string responsibility,
        string statusText,
        string accentBrush,
        string badgeBackground,
        bool isParallelCandidate)
    {
        RoleKey = roleKey;
        DisplayName = displayName;
        Responsibility = responsibility;
        _statusText = statusText;
        AccentBrush = accentBrush;
        BadgeBackground = badgeBackground;
        IsParallelCandidate = isParallelCandidate;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RoleKey { get; }

    public string DisplayName { get; }

    public string Responsibility { get; }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    public string AccentBrush { get; }

    public string BadgeBackground { get; }

    public bool IsParallelCandidate { get; }

    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();
}

public sealed class AgentCouncilEventViewModel
{
    public AgentCouncilEventViewModel(
        string speaker,
        string title,
        string detail,
        string badgeText,
        string accentBrush,
        string badgeBackground)
    {
        Speaker = speaker;
        Title = title;
        Detail = string.IsNullOrWhiteSpace(detail) ? "No detail." : detail;
        BadgeText = badgeText;
        AccentBrush = accentBrush;
        BadgeBackground = badgeBackground;
    }

    public string CreatedAtText { get; } = DateTime.Now.ToString("HH:mm:ss");

    public string Speaker { get; }

    public string Title { get; }

    public string Detail { get; }

    public string BadgeText { get; }

    public string AccentBrush { get; }

    public string BadgeBackground { get; }
}
