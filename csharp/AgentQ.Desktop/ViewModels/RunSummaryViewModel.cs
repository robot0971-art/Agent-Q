using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class RunSummaryViewModel : INotifyPropertyChanged
{
    private string _phase = "Idle";
    private string _nextAction = "Open a project, analyze it, then send a request.";
    private string _lastEvidence = "Evidence will appear after AgentQ reads files, searches, runs tools, or verifies changes.";
    private string _verificationStatus = "Not verified";
    private string _commitReadiness = "No changes";
    private string _changedFilesText = "0 changed";
    private string _timingText = "No timing yet";
    private string _accentBrush = "#B7C4D1";
    private string _phaseBadgeBackground = "#13202D";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Phase
    {
        get => _phase;
        set => SetField(ref _phase, value);
    }

    public string NextAction
    {
        get => _nextAction;
        set => SetField(ref _nextAction, value);
    }

    public string LastEvidence
    {
        get => _lastEvidence;
        set => SetField(ref _lastEvidence, value);
    }

    public string VerificationStatus
    {
        get => _verificationStatus;
        set => SetField(ref _verificationStatus, value);
    }

    public string CommitReadiness
    {
        get => _commitReadiness;
        set => SetField(ref _commitReadiness, value);
    }

    public string ChangedFilesText
    {
        get => _changedFilesText;
        set => SetField(ref _changedFilesText, value);
    }

    public string TimingText
    {
        get => _timingText;
        set => SetField(ref _timingText, value);
    }

    public string AccentBrush
    {
        get => _accentBrush;
        set => SetField(ref _accentBrush, value);
    }

    public string PhaseBadgeBackground
    {
        get => _phaseBadgeBackground;
        set => SetField(ref _phaseBadgeBackground, value);
    }

    public void Update(
        AgentRunState state,
        string statusText,
        IReadOnlyList<AgentRunStep> runSteps,
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<VerificationResultCard> verificationResults,
        bool isBusy,
        bool useKoreanUi = false)
    {
        Phase = DesktopLocalizer.RunSummaryPhase(state, statusText, isBusy, useKoreanUi);
        LastEvidence = BuildEvidence(runSteps, statusText, useKoreanUi);
        VerificationStatus = BuildVerificationStatus(verificationResults, useKoreanUi);
        ChangedFilesText = DesktopLocalizer.ChangedFiles(fileChanges.Count, useKoreanUi);
        TimingText = BuildTimingText(runSteps, isBusy, useKoreanUi);
        CommitReadiness = BuildCommitReadiness(fileChanges, verificationResults, useKoreanUi);
        NextAction = BuildNextAction(state, statusText, fileChanges, verificationResults, isBusy, useKoreanUi);
        AccentBrush = PickAccent(state, statusText, verificationResults);
        PhaseBadgeBackground = PickBackground(AccentBrush);
    }

    public void Reset()
    {
        Phase = "Idle";
        NextAction = "Open a project, analyze it, then send a request.";
        LastEvidence = DesktopLocalizer.NoEvidence(useKoreanUi: false);
        VerificationStatus = DesktopLocalizer.NotVerified(useKoreanUi: false);
        CommitReadiness = DesktopLocalizer.CommitReadinessNoChanges(useKoreanUi: false);
        ChangedFilesText = DesktopLocalizer.ChangedFiles(0, useKoreanUi: false);
        TimingText = DesktopLocalizer.NoTiming(useKoreanUi: false);
        AccentBrush = "#B7C4D1";
        PhaseBadgeBackground = "#13202D";
    }

    private static string BuildEvidence(IReadOnlyList<AgentRunStep> runSteps, string statusText, bool useKoreanUi)
    {
        var lastStep = runSteps.LastOrDefault();
        if (lastStep != null)
        {
            var title = useKoreanUi ? lastStep.DisplayTitle : lastStep.Title;
            return string.IsNullOrWhiteSpace(lastStep.Detail)
                ? title
                : $"{title}: {lastStep.Detail}";
        }

        return string.IsNullOrWhiteSpace(statusText)
            ? DesktopLocalizer.NoEvidence(useKoreanUi)
            : statusText;
    }

    private static string BuildVerificationStatus(IReadOnlyList<VerificationResultCard> verificationResults, bool useKoreanUi)
    {
        var last = verificationResults.FirstOrDefault();
        return last == null
            ? DesktopLocalizer.NotVerified(useKoreanUi)
            : $"{last.Status}: {last.Title}";
    }

    private static string BuildCommitReadiness(
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<VerificationResultCard> verificationResults,
        bool useKoreanUi)
    {
        if (fileChanges.Count == 0)
        {
            return DesktopLocalizer.CommitReadinessNoChanges(useKoreanUi);
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.NeedsEdit))
        {
            return DesktopLocalizer.CommitReadinessNeedsEdit(useKoreanUi);
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.Pending))
        {
            return DesktopLocalizer.CommitReadinessReviewChanges(useKoreanUi);
        }

        var lastVerification = verificationResults.FirstOrDefault();
        if (lastVerification?.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DesktopLocalizer.CommitReadinessReady(useKoreanUi);
        }

        return DesktopLocalizer.CommitReadinessVerify(useKoreanUi);
    }

    private static string BuildTimingText(IReadOnlyList<AgentRunStep> runSteps, bool isBusy, bool useKoreanUi)
    {
        if (runSteps.Count == 0)
        {
            return DesktopLocalizer.NoTiming(useKoreanUi);
        }

        var startedAt = runSteps.Min(step => step.CreatedAt);
        var lastAt = isBusy ? DateTime.Now : runSteps.Max(step => step.CreatedAt);
        var elapsed = lastAt - startedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return DesktopLocalizer.Timing(FormatDuration(elapsed), runSteps.Count, useKoreanUi);
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        return elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes:0}m {elapsed.Seconds:00}s"
            : $"{Math.Max(0, (int)elapsed.TotalSeconds):0}s";
    }

    private static string BuildNextAction(
        AgentRunState state,
        string statusText,
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<VerificationResultCard> verificationResults,
        bool isBusy,
        bool useKoreanUi)
    {
        if (isBusy)
        {
            return state == AgentRunState.WaitingForApproval
                ? DesktopLocalizer.NextActionReviewTool(useKoreanUi)
                : DesktopLocalizer.NextActionWait(useKoreanUi);
        }

        if (DesktopLocalizer.IsProblemStatus(statusText))
        {
            return DesktopLocalizer.NextActionInspectFailure(useKoreanUi);
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.NeedsEdit))
        {
            return DesktopLocalizer.NextActionFixNeedsEdit(useKoreanUi);
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.Pending))
        {
            return DesktopLocalizer.NextActionReviewChanges(useKoreanUi);
        }

        if (fileChanges.Count > 0 && verificationResults.FirstOrDefault()?.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DesktopLocalizer.NextActionCommit(useKoreanUi);
        }

        if (fileChanges.Count > 0)
        {
            return DesktopLocalizer.NextActionVerify(useKoreanUi);
        }

        return DesktopLocalizer.NextActionDefault(useKoreanUi);
    }

    private static string PickAccent(
        AgentRunState state,
        string statusText,
        IReadOnlyList<VerificationResultCard> verificationResults)
    {
        if (DesktopLocalizer.IsProblemStatus(statusText) || state == AgentRunState.Failed)
        {
            return "#F87171";
        }

        var lastVerification = verificationResults.FirstOrDefault();
        if (lastVerification?.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase) == true || state == AgentRunState.Done)
        {
            return "#37D67A";
        }

        if (state is AgentRunState.WaitingForApproval or AgentRunState.Cancelled)
        {
            return "#FBBF24";
        }

        return state is AgentRunState.Idle ? "#B7C4D1" : "#5BA7FF";
    }

    private static string PickBackground(string accentBrush)
    {
        return accentBrush switch
        {
            "#37D67A" => "#062B1A",
            "#F87171" => "#3A1111",
            "#FBBF24" => "#331D03",
            "#5BA7FF" => "#0B2A4A",
            _ => "#13202D"
        };
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
