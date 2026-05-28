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
        Phase = BuildPhase(state, statusText, isBusy, useKoreanUi);
        LastEvidence = BuildEvidence(runSteps, statusText, useKoreanUi);
        VerificationStatus = BuildVerificationStatus(verificationResults, useKoreanUi);
        ChangedFilesText = useKoreanUi ? $"변경 {fileChanges.Count}개" : $"{fileChanges.Count} changed";
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
        LastEvidence = "Evidence will appear after AgentQ reads files, searches, runs tools, or verifies changes.";
        VerificationStatus = "Not verified";
        CommitReadiness = "No changes";
        ChangedFilesText = "0 changed";
        TimingText = "No timing yet";
        AccentBrush = "#B7C4D1";
        PhaseBadgeBackground = "#13202D";
    }

    private static string BuildPhase(AgentRunState state, string statusText, bool isBusy, bool useKoreanUi)
    {
        if (useKoreanUi)
        {
            if (isBusy)
            {
                return state switch
                {
                    AgentRunState.GatheringContext => "컨텍스트 수집",
                    AgentRunState.Generating => "응답 생성",
                    AgentRunState.RunningTool => "도구 실행",
                    AgentRunState.WaitingForApproval => "승인 대기",
                    AgentRunState.RecordingChanges => "변경 기록",
                    AgentRunState.Verifying => "검증 중",
                    AgentRunState.Planning => "계획 중",
                    _ => "실행 중"
                };
            }

            if (IsProblemStatus(statusText))
            {
                return "확인 필요";
            }

            return state switch
            {
                AgentRunState.Done => "완료",
                AgentRunState.Failed => "실패",
                AgentRunState.Cancelled => "취소됨",
                AgentRunState.Idle => "대기",
                _ => state.ToString()
            };
        }

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
            ? useKoreanUi ? "AgentQ가 파일 읽기, 검색, 도구 실행, 검증을 수행하면 근거가 여기에 표시됩니다." : "Evidence will appear after AgentQ reads files, searches, runs tools, or verifies changes."
            : statusText;
    }

    private static string BuildVerificationStatus(IReadOnlyList<VerificationResultCard> verificationResults, bool useKoreanUi)
    {
        var last = verificationResults.FirstOrDefault();
        if (last == null)
        {
            return useKoreanUi ? "검증 안 됨" : "Not verified";
        }

        return $"{last.Status}: {last.Title}";
    }

    private static string BuildCommitReadiness(
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<VerificationResultCard> verificationResults,
        bool useKoreanUi)
    {
        if (fileChanges.Count == 0)
        {
            return useKoreanUi ? "변경 없음" : "No changes";
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.NeedsEdit))
        {
            return useKoreanUi ? "커밋 전 수정 필요" : "Needs edit before commit";
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.Pending))
        {
            return useKoreanUi ? "변경 검토 필요" : "Review changes";
        }

        var lastVerification = verificationResults.FirstOrDefault();
        if (lastVerification?.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase) == true)
        {
            return useKoreanUi ? "커밋 준비됨" : "Ready to commit";
        }

        return useKoreanUi ? "커밋 전 검증 필요" : "Verify before commit";
    }

    private static string BuildTimingText(IReadOnlyList<AgentRunStep> runSteps, bool isBusy, bool useKoreanUi)
    {
        if (runSteps.Count == 0)
        {
            return useKoreanUi ? "아직 시간 정보 없음" : "No timing yet";
        }

        var startedAt = runSteps.Min(step => step.CreatedAt);
        var lastAt = isBusy ? DateTime.Now : runSteps.Max(step => step.CreatedAt);
        var elapsed = lastAt - startedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return useKoreanUi
            ? $"{FormatDuration(elapsed)} 경과 / {runSteps.Count:0}단계"
            : $"{FormatDuration(elapsed)} elapsed / {runSteps.Count:0} step(s)";
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
                ? useKoreanUi ? "요청된 도구 작업을 검토하세요." : "Review the requested tool action."
                : useKoreanUi ? "현재 실행이 끝날 때까지 기다리세요." : "Wait for the current run to finish.";
        }

        if (IsProblemStatus(statusText))
        {
            return useKoreanUi ? "근거 또는 검증 패널에서 실패를 확인하세요." : "Open Evidence or Verify to inspect the failure.";
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.NeedsEdit))
        {
            return useKoreanUi ? "수정 필요로 표시된 변경을 고치세요." : "Fix changes marked as needing edits.";
        }

        if (fileChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.Pending))
        {
            return useKoreanUi ? "변경 파일을 검토한 뒤 검증을 실행하세요." : "Review changed files, then run verification.";
        }

        if (fileChanges.Count > 0 && verificationResults.FirstOrDefault()?.Status.Equals("PASSED", StringComparison.OrdinalIgnoreCase) == true)
        {
            return useKoreanUi ? "커밋 메시지를 준비하고 커밋하세요." : "Prepare the commit message and commit.";
        }

        if (fileChanges.Count > 0)
        {
            return useKoreanUi ? "커밋 전에 집중 검증을 실행하세요." : "Run focused verification before committing.";
        }

        return useKoreanUi ? "요청을 보내거나 프로젝트 분석을 새로고침하세요." : "Send a request or refresh project analysis.";
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
