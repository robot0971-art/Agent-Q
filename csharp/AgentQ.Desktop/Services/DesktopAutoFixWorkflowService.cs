using System.Security.Cryptography;
using System.Text;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopAutoFixWorkflowService(
    DesktopGitService gitService,
    DesktopVerificationPanelWorkflowService verificationPanelWorkflowService)
{
    private readonly List<FileChangeRecord> _pendingAutoFixChanges = [];
    private AgentVerificationPlan? _pendingAutoFixVerificationPlan;
    private int _pendingAutoFixNextAttempt;
    private int _pendingAutoFixMaxAttempts;
    private string _pendingAutoFixPreviousFailureSignature = string.Empty;

    public Task RunAsync(
        MainViewModel viewModel,
        int maxAttempts,
        Func<bool, Task> sendCurrentMessageAsync)
    {
        return RunAsync(
            viewModel,
            maxAttempts,
            startAttempt: 1,
            previousFailureSignature: verificationPanelWorkflowService.LastFailureSignature,
            sendCurrentMessageAsync);
    }

    public async Task ApprovePendingChangesAndVerifyAsync(
        MainViewModel viewModel,
        Func<AgentVerificationPlan, Task<DesktopVerificationWorkflowResult?>> runVerificationPlanAsync,
        Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        if (_pendingAutoFixVerificationPlan == null)
        {
            viewModel.StatusText = "No pending Auto Fix verification";
            viewModel.ClearPendingReviewVerification();
            return;
        }

        if (_pendingAutoFixChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.NeedsEdit))
        {
            viewModel.StatusText = "Auto Fix changes need edits before verification";
            viewModel.AddRunStep(
                AgentRunState.WaitingForApproval,
                "Auto fix waiting for edits",
                "One or more pending changes are marked Needs edit.");
            return;
        }

        if (_pendingAutoFixChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.Reverted))
        {
            ClearPendingReview();
            viewModel.ClearPendingReviewVerification();
            viewModel.StatusText = "Auto Fix verification cancelled after revert";
            viewModel.AddRunStep(
                AgentRunState.Cancelled,
                "Auto fix verification cancelled",
                "One or more pending changes were reverted.");
            return;
        }

        foreach (var change in _pendingAutoFixChanges.Where(change => change.ReviewStatus == FileChangeReviewStatus.Pending))
        {
            change.ReviewStatus = FileChangeReviewStatus.Approved;
        }

        var retryPlan = _pendingAutoFixVerificationPlan;
        var nextAttempt = _pendingAutoFixNextAttempt;
        var maxAttempts = _pendingAutoFixMaxAttempts;
        var previousFailureSignature = _pendingAutoFixPreviousFailureSignature;
        ClearPendingReview();
        viewModel.ClearPendingReviewVerification();

        viewModel.AddRunStep(
            AgentRunState.Verifying,
            "Approved Auto Fix changes",
            retryPlan.Command);
        var verificationResult = await runVerificationPlanAsync(retryPlan);

        if (verificationResult?.Succeeded == true)
        {
            viewModel.AddRunStep(AgentRunState.Done, "Auto fix succeeded", retryPlan.Command);
            viewModel.StatusText = "Auto fix succeeded";
            return;
        }

        var currentFailureSignature = verificationPanelWorkflowService.LastFailureSignature;
        if (!string.IsNullOrWhiteSpace(currentFailureSignature) &&
            string.Equals(previousFailureSignature, currentFailureSignature, StringComparison.Ordinal))
        {
            viewModel.AddRunStep(
                AgentRunState.Failed,
                "Auto fix stopped: repeated failure",
                "The latest verification failed in the same way as before.");
            viewModel.StatusText = "Auto fix stopped: repeated failure";
            return;
        }

        if (nextAttempt > maxAttempts)
        {
            viewModel.AddRunStep(
                AgentRunState.Failed,
                "Auto fix stopped: max attempts reached",
                $"Tried {maxAttempts} fix attempts.");
            viewModel.StatusText = $"Auto fix stopped after {maxAttempts} attempts";
            return;
        }

        await RunAsync(viewModel, maxAttempts, nextAttempt, currentFailureSignature, sendCurrentMessageAsync);
    }

    public void ClearPendingReview()
    {
        _pendingAutoFixVerificationPlan = null;
        _pendingAutoFixChanges.Clear();
        _pendingAutoFixNextAttempt = 0;
        _pendingAutoFixMaxAttempts = 0;
        _pendingAutoFixPreviousFailureSignature = string.Empty;
    }

    private async Task RunAsync(
        MainViewModel viewModel,
        int maxAttempts,
        int startAttempt,
        string previousFailureSignature,
        Func<bool, Task> sendCurrentMessageAsync)
    {
        if (startAttempt > maxAttempts)
        {
            viewModel.AddRunStep(
                AgentRunState.Failed,
                "Auto fix stopped: max attempts reached",
                $"Tried {maxAttempts} fix attempts.");
            viewModel.StatusText = $"Auto fix stopped after {maxAttempts} attempts";
            return;
        }

        var retryPlan = verificationPanelWorkflowService.CreateRetryPlan();
        var fixPrompt = verificationPanelWorkflowService.BuildFixPrompt();
        if (retryPlan == null || string.IsNullOrWhiteSpace(fixPrompt))
        {
            viewModel.StatusText = startAttempt == 1
                ? "No failed verification to auto-fix"
                : "Auto fix stopped: no failed verification remains";
            return;
        }

        var fileChangeCountBeforeAttempt = viewModel.FileChanges.Count;
        var workspaceFingerprintBeforeAttempt = await BuildWorkspaceChangeFingerprintAsync(viewModel.WorkspaceRoot);

        viewModel.AddRunStep(
            AgentRunState.Planning,
            $"Auto fix attempt {startAttempt}/{maxAttempts}",
            $"Fix, then rerun: {retryPlan.Command}");
        viewModel.InputText = fixPrompt;
        await sendCurrentMessageAsync(true);

        if (viewModel.IsBusy)
        {
            return;
        }

        var recordedFileChangeCount = viewModel.FileChanges.Count - fileChangeCountBeforeAttempt;
        var workspaceFingerprintAfterAttempt = await BuildWorkspaceChangeFingerprintAsync(viewModel.WorkspaceRoot);
        if (recordedFileChangeCount <= 0 &&
            string.Equals(workspaceFingerprintBeforeAttempt, workspaceFingerprintAfterAttempt, StringComparison.Ordinal))
        {
            viewModel.AddRunStep(
                AgentRunState.Failed,
                "Auto fix stopped: no file changes",
                "The fix attempt did not change the workspace.");
            viewModel.StatusText = "Auto fix stopped: no file changes";
            return;
        }

        viewModel.AddRunStep(
            AgentRunState.RecordingChanges,
            "Auto fix changes detected",
            recordedFileChangeCount > 0
                ? $"{recordedFileChangeCount} file change(s) recorded."
                : "Workspace diff changed.");

        PauseForReview(
            viewModel,
            retryPlan,
            viewModel.FileChanges.Skip(fileChangeCountBeforeAttempt).ToList(),
            startAttempt + 1,
            maxAttempts,
            previousFailureSignature);
    }

    private void PauseForReview(
        MainViewModel viewModel,
        AgentVerificationPlan retryPlan,
        IReadOnlyList<FileChangeRecord> changes,
        int nextAttempt,
        int maxAttempts,
        string previousFailureSignature)
    {
        _pendingAutoFixVerificationPlan = retryPlan;
        _pendingAutoFixChanges.Clear();
        _pendingAutoFixChanges.AddRange(changes);
        _pendingAutoFixNextAttempt = nextAttempt;
        _pendingAutoFixMaxAttempts = maxAttempts;
        _pendingAutoFixPreviousFailureSignature = previousFailureSignature;
        viewModel.SetPendingReviewVerification(retryPlan, changes.Count, nextAttempt, maxAttempts);

        viewModel.AddRunStep(
            AgentRunState.WaitingForApproval,
            "Auto fix paused for review",
            "Review the changed files in Preview, then choose Approve all & verify.");
        viewModel.StatusText = "Review Auto Fix changes before verification";
    }

    private async Task<string> BuildWorkspaceChangeFingerprintAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var status = await gitService.GetStatusAsync(workspaceRoot, ct);
        var diff = await gitService.GetFullDiffAsync(workspaceRoot, ct);
        var content = $"{status.ExitCode}\n{status.StandardOutput}\n{status.StandardError}\n---diff---\n{diff.ExitCode}\n{diff.StandardOutput}\n{diff.StandardError}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}
