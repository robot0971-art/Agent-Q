using AgentQ.Desktop.ViewModels;
using AgentQ.Desktop.Views;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPanelEventBinder
{
    public void Bind(
        SettingsPanel settingsPanel,
        ProjectPanel projectPanel,
        VerificationPanel verificationPanel,
        PlanPanel planPanel,
        MemoryPanel memoryPanel,
        ChatPanel chatPanel,
        FileChangeReviewPanel fileChangeReviewPanel,
        GitPanel gitPanel,
        DesktopPanelEventCallbacks callbacks)
    {
        settingsPanel.SaveRequested += async (_, _) => await callbacks.SaveSettingsAsync();
        settingsPanel.ApiKeyChanged += (_, apiKey) => callbacks.UpdateApiKey(apiKey);
        settingsPanel.EmbeddingApiKeyChanged += (_, apiKey) => callbacks.UpdateEmbeddingApiKey(apiKey);

        projectPanel.BrowseWorkspaceRequested += async (_, _) => await callbacks.BrowseWorkspaceAsync();
        projectPanel.OpenWorkspaceRequested += (_, _) => callbacks.OpenWorkspace();
        projectPanel.RefreshWorkspaceAnalysisRequested += async (_, _) => await callbacks.RefreshWorkspaceAnalysisAsync();
        projectPanel.BuildEmbeddingIndexRequested += async (_, _) => await callbacks.BuildEmbeddingIndexAsync();
        projectPanel.CopyAnalysisReportRequested += (_, _) => callbacks.CopyAnalysisReport();
        projectPanel.SaveAnalysisReportRequested += async (_, _) => await callbacks.SaveAnalysisReportAsync();
        projectPanel.SaveProjectConfigRequested += async (_, _) => await callbacks.SaveProjectConfigAsync();
        projectPanel.LoadProjectConfigRequested += async (_, _) => await callbacks.LoadProjectConfigAsync();

        verificationPanel.RunRequested += async (_, plan) => await callbacks.RunVerificationPlanAsync(plan);
        verificationPanel.FixFailureRequested += async (_, _) => await callbacks.FixVerificationFailureAsync();
        verificationPanel.AutoFixRequested += async (_, _) => await callbacks.AutoFixVerificationFailureAsync();

        planPanel.CreatePlanRequested += (_, _) => callbacks.CreatePlan();
        planPanel.ContinuePlanItemRequested += (_, _) => callbacks.ContinuePlanItem();
        planPanel.MarkPlanItemDoneRequested += (_, _) => callbacks.MarkPlanItemDone();
        planPanel.SaveCheckpointRequested += (_, _) => callbacks.SaveCheckpoint();
        planPanel.LoadCheckpointRequested += (_, _) => callbacks.LoadCheckpoint();
        planPanel.ResumeCheckpointRequested += (_, _) => callbacks.ResumeCheckpoint();
        planPanel.PlanAndRunRequested += (_, _) => callbacks.PlanAndRun();
        planPanel.MarkDoneAndContinueRequested += (_, _) => callbacks.MarkDoneAndContinue();

        memoryPanel.SaveSessionSummaryRequested += (_, _) => callbacks.SaveSessionSummary();
        memoryPanel.LoadSessionSummaryRequested += (_, _) => callbacks.LoadSessionSummary();
        memoryPanel.ResumeSessionSummaryRequested += (_, _) => callbacks.ResumeSessionSummary();
        memoryPanel.SaveSelectedLessonRequested += async (_, lesson) => await callbacks.SaveSelectedMemoryLessonAsync(lesson);
        memoryPanel.DismissSelectedLessonRequested += (_, lesson) => callbacks.DismissSelectedMemoryLesson(lesson);
        memoryPanel.RefreshSavedMemoryRequested += async (_, _) => await callbacks.RefreshSavedMemoryAsync();
        memoryPanel.DisableSavedMemoryRequested += async (_, lesson) => await callbacks.DisableSavedMemoryAsync(lesson);
        memoryPanel.DeleteSavedMemoryRequested += async (_, lesson) => await callbacks.DeleteSavedMemoryAsync(lesson);

        chatPanel.AttachFilesRequested += (_, _) => callbacks.AttachFiles();
        chatPanel.BrowseWorkspaceRequested += async (_, _) => await callbacks.BrowseWorkspaceAsync();
        chatPanel.ClearAttachmentsRequested += (_, _) => callbacks.ClearAttachments();
        chatPanel.SendRequested += async (_, _) => await callbacks.SendCurrentMessageAsync();
        chatPanel.ContinueLastRunRequested += (_, _) => callbacks.ContinueLastRun();
        chatPanel.StopAgentRequested += (_, _) => callbacks.StopAgent();
        chatPanel.CopyMessageRequested += (_, message) => callbacks.CopyMessage(message as ChatMessageViewModel);

        fileChangeReviewPanel.ApproveRequested += (_, record) => callbacks.ApproveFileChange(record);
        fileChangeReviewPanel.NeedsEditRequested += (_, record) => callbacks.MarkFileChangeNeedsEdit(record);
        fileChangeReviewPanel.RevertRequested += async (_, record) => await callbacks.RevertFileChangeAsync(record);
        fileChangeReviewPanel.ApproveAllAndVerifyRequested += async (_, _) => await callbacks.ApproveAllFileChangesAndVerifyAsync();

        gitPanel.StatusRequested += async (_, _) => await callbacks.RefreshGitStatusAsync();
        gitPanel.DiffRequested += async (_, _) => await callbacks.RefreshGitDiffAsync();
        gitPanel.ReviewRequested += async (_, _) => await callbacks.ReviewGitChangesAsync();
        gitPanel.FixReviewRequested += async (_, _) => await callbacks.FixCodeReviewFindingsAsync();
        gitPanel.CommitSummaryRequested += async (_, _) => await callbacks.CommitSummaryAsync();
        gitPanel.PullFastForwardRequested += async (_, _) => await callbacks.PullFastForwardAsync();
        gitPanel.BackupBranchRequested += async (_, _) => await callbacks.CreateBackupBranchAsync();
        gitPanel.CheckoutMainRequested += async (_, _) => await callbacks.CheckoutMainAsync();
        gitPanel.SelectedFileChanged += async (_, _) => await callbacks.LoadSelectedGitFileDiffAsync();
        gitPanel.ApproveRequested += (_, _) => callbacks.ApproveGitChange();
        gitPanel.RejectRequested += (_, _) => callbacks.RejectGitChange();
        gitPanel.NeedsEditRequested += (_, _) => callbacks.MarkGitChangeNeedsEdit();
        gitPanel.StageSelectedRequested += async (_, _) => await callbacks.StageSelectedGitFileAsync();
        gitPanel.StageApprovedRequested += async (_, _) => await callbacks.StageApprovedGitFilesAsync();
        gitPanel.UnstageSelectedRequested += async (_, _) => await callbacks.UnstageSelectedGitFileAsync();
        gitPanel.CommitStagedRequested += async (_, _) => await callbacks.CommitStagedGitFilesAsync();
    }
}

public sealed class DesktopPanelEventCallbacks
{
    public required Func<Task> SaveSettingsAsync { get; init; }
    public required Action<string> UpdateApiKey { get; init; }
    public required Action<string> UpdateEmbeddingApiKey { get; init; }
    public required Func<Task> BrowseWorkspaceAsync { get; init; }
    public required Action OpenWorkspace { get; init; }
    public required Func<Task> RefreshWorkspaceAnalysisAsync { get; init; }
    public required Func<Task> BuildEmbeddingIndexAsync { get; init; }
    public required Action CopyAnalysisReport { get; init; }
    public required Func<Task> SaveAnalysisReportAsync { get; init; }
    public required Func<Task> SaveProjectConfigAsync { get; init; }
    public required Func<Task> LoadProjectConfigAsync { get; init; }
    public required Func<AgentVerificationPlan, Task> RunVerificationPlanAsync { get; init; }
    public required Func<Task> FixVerificationFailureAsync { get; init; }
    public required Func<Task> AutoFixVerificationFailureAsync { get; init; }
    public required Action CreatePlan { get; init; }
    public required Action ContinuePlanItem { get; init; }
    public required Action MarkPlanItemDone { get; init; }
    public required Action SaveCheckpoint { get; init; }
    public required Action LoadCheckpoint { get; init; }
    public required Action ResumeCheckpoint { get; init; }
    public required Action PlanAndRun { get; init; }
    public required Action MarkDoneAndContinue { get; init; }
    public required Action SaveSessionSummary { get; init; }
    public required Action LoadSessionSummary { get; init; }
    public required Action ResumeSessionSummary { get; init; }
    public required Func<ProjectMemoryLesson?, Task> SaveSelectedMemoryLessonAsync { get; init; }
    public required Action<ProjectMemoryLesson?> DismissSelectedMemoryLesson { get; init; }
    public required Func<Task> RefreshSavedMemoryAsync { get; init; }
    public required Func<ProjectMemoryLesson?, Task> DisableSavedMemoryAsync { get; init; }
    public required Func<ProjectMemoryLesson?, Task> DeleteSavedMemoryAsync { get; init; }
    public required Action AttachFiles { get; init; }
    public required Action ClearAttachments { get; init; }
    public required Func<Task> SendCurrentMessageAsync { get; init; }
    public required Action ContinueLastRun { get; init; }
    public required Action StopAgent { get; init; }
    public required Action<ChatMessageViewModel?> CopyMessage { get; init; }
    public required Action<FileChangeRecord?> ApproveFileChange { get; init; }
    public required Action<FileChangeRecord?> MarkFileChangeNeedsEdit { get; init; }
    public required Func<FileChangeRecord?, Task> RevertFileChangeAsync { get; init; }
    public required Func<Task> ApproveAllFileChangesAndVerifyAsync { get; init; }
    public required Func<Task> RefreshGitStatusAsync { get; init; }
    public required Func<Task> RefreshGitDiffAsync { get; init; }
    public required Func<Task> ReviewGitChangesAsync { get; init; }
    public required Func<Task> FixCodeReviewFindingsAsync { get; init; }
    public required Func<Task> CommitSummaryAsync { get; init; }
    public required Func<Task> PullFastForwardAsync { get; init; }
    public required Func<Task> CreateBackupBranchAsync { get; init; }
    public required Func<Task> CheckoutMainAsync { get; init; }
    public required Func<Task> LoadSelectedGitFileDiffAsync { get; init; }
    public required Action ApproveGitChange { get; init; }
    public required Action RejectGitChange { get; init; }
    public required Action MarkGitChangeNeedsEdit { get; init; }
    public required Func<Task> StageSelectedGitFileAsync { get; init; }
    public required Func<Task> StageApprovedGitFilesAsync { get; init; }
    public required Func<Task> UnstageSelectedGitFileAsync { get; init; }
    public required Func<Task> CommitStagedGitFilesAsync { get; init; }
}
