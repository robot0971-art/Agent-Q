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
        bool isBusy)
    {
        Phase = BuildPhase(state, statusText, isBusy);
        LastEvidence = BuildEvidence(runSteps, statusText);
        VerificationStatus = BuildVerificationStatus(verificationResults);
        ChangedFilesText = $"{fileChanges.Count} changed";
        CommitReadiness = BuildCommitReadiness(fileChanges, verificationResults);
        NextAction = BuildNextAction(state, statusText, fileChanges, verificationResults, isBusy);
        AccentBrush = PickAccent(state, statusText, verificationResults);
        PhaseBadgeBackground = PickBackground(AccentBrush);
    }

    public void Reset()
    {
        Phase = "Idle";
        NextAction = "Open a project, analyze it, then send a request.";
        LastEvidence = "Evidence will appear after AgentQ reads files, searches, runs tools, or verifies changes.";
        VerificationStatus = "Not verified";
        CommitReadiness = "No changes";
        ChangedFilesText = "0 changed";
        AccentBrush = "#B7C4D1";
        PhaseBadgeBackground = "#13202D";
    }

    private static string BuildPhase(AgentRunState state, string statusText, bool isBusy)
    {
        if (isBusy)
        {
            return state switch
            {
                AgentRunState.GatheringContext => "Gathering context",
                AgentRunState.Generating => "Generating",
                AgentRunState.RunningTool => "Running tool",
                AgentRunState.WaitingForApproval => "Waiting for approval",
                AgentRunState.RecordingChanges => "Recording changes",
                AgentRunState.Verifying => "Verifying",
                AgentRunState.Planning => "Planning",
                _ => "Running"
            };
        }

        if (IsProblemStatus(statusText))
        {
            return "Needs attention";
        }

        return state switch
        {
            AgentRunState.Done => "Completed",
            AgentRunState.Failed => "Failed",
            AgentRunState.Cancelled => "Cancelled",
            AgentRunState.Idle => "Idle",
            _ => state.ToString()
        };
    }

    private static string BuildEvidence(IReadOnlyList<AgentRunStep> runSteps, string statusText)
    {
        var lastStep = runSteps.LastOrDefault();
        if (lastStep != null)
        {
            return string.IsNullOrWhiteSpace(lastStep.Detail)
                ? lastStep.Title
                : $"{lastStep.Title}: {lastStep.Detail}";
        }

        return string.IsNullOrWhiteSpace(statusText)
            ? "Evidence will appear after AgentQ reads files, searches, runs tools, or verifies changes."
            : statusText;
    }

    private static string BuildVerificationStatus(IReadOnlyList<VerificationResultCard> verificationResults)
    {
        var last = verificationResults.FirstOrDefault();
        if (last == null)
        {
            return "Not verified";
        }

        return $"{last.Status}: {last.Title}";
    }

    private static string BuildCommitReadiness(
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<VerificationResultCard> verificationResults)
    {
        if (fileChanges.Count == 0)
        {
            return "No changes";
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.NeedsEdit))
        {
            return "Needs edit before commit";
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.Pending))
        {
            return "Review changes";
        }

        var lastVerification = verificationResults.FirstOrDefault();
        if (lastVerification?.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Ready to commit";
        }

        return "Verify before commit";
    }

    private static string BuildNextAction(
        AgentRunState state,
        string statusText,
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<VerificationResultCard> verificationResults,
        bool isBusy)
    {
        if (isBusy)
        {
            return state == AgentRunState.WaitingForApproval
                ? "Review the requested tool action."
                : "Wait for the current run to finish.";
        }

        if (IsProblemStatus(statusText))
        {
            return "Open Evidence or Verify to inspect the failure.";
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.NeedsEdit))
        {
            return "Fix changes marked as needing edits.";
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.Pending))
        {
            return "Review changed files, then run verification.";
        }

        if (fileChanges.Count > 0 && verificationResults.FirstOrDefault()?.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Prepare the commit message and commit.";
        }

        if (fileChanges.Count > 0)
        {
            return "Run focused verification before committing.";
        }

        return "Send a request or refresh project analysis.";
    }

    private static string PickAccent(
        AgentRunState state,
        string statusText,
        IReadOnlyList<VerificationResultCard> verificationResults)
    {
        if (IsProblemStatus(statusText) || state == AgentRunState.Failed)
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

    private static bool IsProblemStatus(string statusText)
    {
        return statusText.Contains("error", StringComparison.OrdinalIgnoreCase) ||
               statusText.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               statusText.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
               statusText.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
               statusText.Contains("denied", StringComparison.OrdinalIgnoreCase);
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
