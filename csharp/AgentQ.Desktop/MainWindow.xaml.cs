using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AgentQ.Core.Providers;
using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop;

public partial class MainWindow : Window
{
    private const double MouseWheelScrollFactor = 0.35;

    private readonly MainViewModel _viewModel;
    private readonly DesktopConfigService _configService;
    private readonly DesktopVerificationPanelWorkflowService _verificationPanelWorkflowService;
    private readonly DesktopGitPanelWorkflowService _gitPanelWorkflowService;
    private readonly DesktopPlanCheckpointWorkflowService _planCheckpointWorkflowService;
    private readonly DesktopWorkspaceContextWorkflowService _workspaceContextWorkflowService;
    private readonly DesktopAgentRunWorkflowService _agentRunWorkflowService;
    private readonly DesktopFileChangeReviewService _fileChangeReviewService;
    private readonly DesktopCheckpointWorkflowService _checkpointWorkflowService;
    private readonly DesktopAttachmentSelectionService _attachmentSelectionService;
    private readonly DesktopClipboardService _clipboardService;
    private readonly DesktopAutoFixWorkflowService _autoFixWorkflowService;
    private readonly List<DesktopAttachment> _attachments = [];

    public MainWindow(
        MainViewModel viewModel,
        DesktopConfigService configService,
        DesktopVerificationPanelWorkflowService verificationPanelWorkflowService,
        DesktopGitPanelWorkflowService gitPanelWorkflowService,
        DesktopPlanCheckpointWorkflowService planCheckpointWorkflowService,
        DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService,
        DesktopAgentRunWorkflowService agentRunWorkflowService,
        DesktopFileChangeReviewService fileChangeReviewService,
        DesktopCheckpointWorkflowService checkpointWorkflowService,
        DesktopAttachmentSelectionService attachmentSelectionService,
        DesktopClipboardService clipboardService,
        DesktopAutoFixWorkflowService autoFixWorkflowService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _configService = configService;
        _verificationPanelWorkflowService = verificationPanelWorkflowService;
        _gitPanelWorkflowService = gitPanelWorkflowService;
        _planCheckpointWorkflowService = planCheckpointWorkflowService;
        _workspaceContextWorkflowService = workspaceContextWorkflowService;
        _agentRunWorkflowService = agentRunWorkflowService;
        _fileChangeReviewService = fileChangeReviewService;
        _checkpointWorkflowService = checkpointWorkflowService;
        _attachmentSelectionService = attachmentSelectionService;
        _clipboardService = clipboardService;
        _autoFixWorkflowService = autoFixWorkflowService;
        DataContext = _viewModel;
        HookSettingsPanelEvents();
        HookVerificationPanelEvents();
        HookPlanPanelEvents();
        HookMemoryPanelEvents();
        HookChatPanelEvents();
        HookGitPanelEvents();
        _viewModel.Messages.CollectionChanged += (_, _) => ChatPanelView.ScrollMessagesToEndIfPinned();
        Loaded += MainWindow_OnLoaded;
    }

    private void HookSettingsPanelEvents()
    {
        SettingsPanelView.SaveRequested += (_, _) => SaveSettings_OnClick(this, new RoutedEventArgs());
        SettingsPanelView.ApiKeyChanged += (_, apiKey) => _viewModel.ApiKey = apiKey;
    }

    private void HookVerificationPanelEvents()
    {
        VerificationPanelView.RunRequested += async (_, plan) => await RunVerificationPlanAsync(plan);
        VerificationPanelView.FixFailureRequested += (_, _) => FixVerificationFailure_OnClick(this, new RoutedEventArgs());
        VerificationPanelView.AutoFixRequested += (_, _) => AutoFixVerificationFailure_OnClick(this, new RoutedEventArgs());
    }

    private void HookPlanPanelEvents()
    {
        PlanPanelView.CreatePlanRequested += (_, _) => CreatePlan_OnClick(this, new RoutedEventArgs());
        PlanPanelView.ContinuePlanItemRequested += (_, _) => ContinuePlanItem_OnClick(this, new RoutedEventArgs());
        PlanPanelView.MarkPlanItemDoneRequested += (_, _) => MarkPlanItemDone_OnClick(this, new RoutedEventArgs());
        PlanPanelView.SaveCheckpointRequested += (_, _) => SaveCheckpoint_OnClick(this, new RoutedEventArgs());
        PlanPanelView.LoadCheckpointRequested += (_, _) => LoadCheckpoint_OnClick(this, new RoutedEventArgs());
        PlanPanelView.ResumeCheckpointRequested += (_, _) => ResumeCheckpoint_OnClick(this, new RoutedEventArgs());
        PlanPanelView.PlanAndRunRequested += (_, _) => PlanAndRun_OnClick(this, new RoutedEventArgs());
        PlanPanelView.MarkDoneAndContinueRequested += (_, _) => MarkDoneAndContinue_OnClick(this, new RoutedEventArgs());
    }

    private void HookMemoryPanelEvents()
    {
        MemoryPanelView.SaveSessionSummaryRequested += (_, _) => SaveSessionSummary_OnClick(this, new RoutedEventArgs());
        MemoryPanelView.LoadSessionSummaryRequested += (_, _) => LoadSessionSummary_OnClick(this, new RoutedEventArgs());
        MemoryPanelView.ResumeSessionSummaryRequested += (_, _) => ResumeSessionSummary_OnClick(this, new RoutedEventArgs());
    }

    private void HookChatPanelEvents()
    {
        ChatPanelView.AttachFilesRequested += (_, _) => AttachFiles_OnClick(this, new RoutedEventArgs());
        ChatPanelView.BrowseWorkspaceRequested += (_, _) => BrowseWorkspace_OnClick(this, new RoutedEventArgs());
        ChatPanelView.ClearAttachmentsRequested += (_, _) => ClearAttachments_OnClick(this, new RoutedEventArgs());
        ChatPanelView.SendRequested += async (_, _) => await SendCurrentMessageAsync();
        ChatPanelView.ContinueLastRunRequested += (_, _) => ContinueLastRun_OnClick(this, new RoutedEventArgs());
        ChatPanelView.StopAgentRequested += (_, _) => StopAgent_OnClick(this, new RoutedEventArgs());
        ChatPanelView.CopyMessageRequested += (_, message) =>
            _clipboardService.CopyMessage(_viewModel, message as ChatMessageViewModel);
    }

    private void HookGitPanelEvents()
    {
        GitPanelView.StatusRequested += async (_, _) => await RefreshGitStatusAsync();
        GitPanelView.DiffRequested += async (_, _) => await RefreshGitDiffAsync();
        GitPanelView.ReviewRequested += (_, _) => ReviewGitChanges_OnClick(this, new RoutedEventArgs());
        GitPanelView.FixReviewRequested += (_, _) => FixCodeReviewFindings_OnClick(this, new RoutedEventArgs());
        GitPanelView.CommitSummaryRequested += (_, _) => CommitSummary_OnClick(this, new RoutedEventArgs());
        GitPanelView.PullFastForwardRequested += async (_, _) => await _gitPanelWorkflowService.PullFastForwardOnlyAsync(_viewModel, TrimForLog);
        GitPanelView.SelectedFileChanged += async (_, _) => await _gitPanelWorkflowService.LoadSelectedFileDiffAsync(_viewModel);
        GitPanelView.ApproveRequested += (_, _) => _gitPanelWorkflowService.SetSelectedReviewStatus(_viewModel, GitChangeReviewStatus.Approved);
        GitPanelView.RejectRequested += (_, _) => _gitPanelWorkflowService.SetSelectedReviewStatus(_viewModel, GitChangeReviewStatus.Rejected);
        GitPanelView.NeedsEditRequested += (_, _) => _gitPanelWorkflowService.SetSelectedReviewStatus(_viewModel, GitChangeReviewStatus.NeedsEdit);
        GitPanelView.StageSelectedRequested += async (_, _) => await _gitPanelWorkflowService.StageSelectedFileAsync(_viewModel, TrimForLog);
        GitPanelView.StageApprovedRequested += async (_, _) => await _gitPanelWorkflowService.StageApprovedFilesAsync(_viewModel, TrimForLog);
        GitPanelView.UnstageSelectedRequested += async (_, _) => await _gitPanelWorkflowService.UnstageSelectedFileAsync(_viewModel, TrimForLog);
        GitPanelView.CommitStagedRequested += async (_, _) => await _gitPanelWorkflowService.CommitAsync(_viewModel, TrimForLog);
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var saved = await _configService.LoadAsync();
        if (saved != null)
        {
            _viewModel.ApplyConfiguration(saved);
            SettingsPanelView.ApiKey = saved.ApiKey;
            _viewModel.StatusText = "Settings loaded";
        }
        else
        {
            _viewModel.ApplyConfiguration(new ProviderConfiguration
            {
                Provider = "opencode-go",
                Model = "kimi-k2.6",
                BaseUrl = ProviderConfiguration.OpenCodeGoDefaultBaseUrl,
                TimeoutSeconds = 30,
                MaxTokens = 4096
            });
            _viewModel.StatusText = "First run: enter an API key, confirm provider/model, then save settings.";
            _viewModel.AddLog("First run setup: enter an API key in Settings and click Save.");
        }

        _viewModel.AddLog("AgentQ Desktop started");
        await _workspaceContextWorkflowService.LoadWorkspaceContextAsync(_viewModel, TrimForLog);
    }

    private async void SaveSettings_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _configService.SaveAsync(_viewModel.ToConfiguration());
            _viewModel.StatusText = "Settings saved";
            _viewModel.AddLog("Settings saved");
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Settings save failed: {ex.Message}";
            _viewModel.AddLog($"Settings save failed: {ex.Message}");
        }
    }

    private async void BrowseWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a project folder.",
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(_viewModel.WorkspaceRoot)
                ? Environment.CurrentDirectory
                : _viewModel.WorkspaceRoot
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _viewModel.WorkspaceRoot = dialog.SelectedPath;
            await _workspaceContextWorkflowService.LoadWorkspaceContextAsync(_viewModel, TrimForLog);
            _viewModel.StatusText = "Project folder selected";
            _viewModel.AddLog($"Project folder selected: {dialog.SelectedPath}");
        }
    }

    private async void RefreshWorkspaceAnalysis_OnClick(object sender, RoutedEventArgs e)
    {
        await _workspaceContextWorkflowService.RefreshWorkspaceAnalysisAsync(_viewModel, TrimForLog);
    }

    private async void SaveProjectConfig_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _workspaceContextWorkflowService.SaveProjectConfigAsync(_viewModel);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Project config save failed: {ex.Message}";
            _viewModel.AddLog($"Project config save failed: {TrimForLog(ex.Message)}");
        }
    }

    private async void LoadProjectConfig_OnClick(object sender, RoutedEventArgs e)
    {
        await _workspaceContextWorkflowService.LoadProjectConfigAsync(_viewModel);
        _viewModel.StatusText = _workspaceContextWorkflowService.ProjectConfig == null
            ? "No project config found"
            : "Project config loaded";
    }

    private void OpenWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_viewModel.WorkspaceRoot))
        {
            _viewModel.StatusText = "No valid project folder to open.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _viewModel.WorkspaceRoot,
            UseShellExecute = true
        });
    }

    private void AttachFiles_OnClick(object sender, RoutedEventArgs e)
    {
        _attachmentSelectionService.SelectAttachments(this, _viewModel, _attachments);
    }

    private void ClearAttachments_OnClick(object sender, RoutedEventArgs e)
    {
        _attachmentSelectionService.ClearAttachments(_viewModel, _attachments);
    }

    private async void Send_OnClick(object sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void ContinueLastRun_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_agentRunWorkflowService.PrepareContinuation(_viewModel))
        {
            return;
        }

        await SendCurrentMessageAsync(preserveLastVerificationFailure: true);
    }

    private void StopAgent_OnClick(object sender, RoutedEventArgs e)
    {
        _agentRunWorkflowService.Stop(_viewModel);
    }

    private void IncreaseFontSize_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.DesktopFontSize += 1;
        _viewModel.StatusText = $"Font size: {_viewModel.DesktopFontSize:0}";
    }

    private void DecreaseFontSize_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.DesktopFontSize = Math.Max(11, _viewModel.DesktopFontSize - 1);
        _viewModel.StatusText = $"Font size: {_viewModel.DesktopFontSize:0}";
    }

    private void ResetFontSize_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.DesktopFontSize = 14;
        _viewModel.StatusText = "Font size reset";
    }

    private void ShowStatus_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.StatusText =
            $"Provider: {_viewModel.Provider}, Model: {_viewModel.Model}, Font size: {_viewModel.DesktopFontSize:0}";
    }

    private void CopyLastAssistantMessage_OnClick(object sender, RoutedEventArgs e)
    {
        _clipboardService.CopyLastAssistantMessage(_viewModel);
    }

    private void CopyConversation_OnClick(object sender, RoutedEventArgs e)
    {
        _clipboardService.CopyConversation(_viewModel);
    }

    private void SmoothScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var scrollViewer = source is ScrollViewer viewer
            ? viewer
            : FindDescendant<ScrollViewer>(source);
        if (scrollViewer == null)
        {
            return;
        }

        e.Handled = true;
        SmoothScroll(scrollViewer, e.Delta);
    }

    private static void SmoothScroll(ScrollViewer scrollViewer, int wheelDelta)
    {
        var targetOffset = scrollViewer.VerticalOffset - wheelDelta * MouseWheelScrollFactor;
        targetOffset = Math.Clamp(targetOffset, 0, scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(targetOffset);
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void ClearLogs_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Logs.Clear();
        _viewModel.AddLog("Logs cleared");
    }

    private void ClearSidePanel_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Logs.Clear();
        _viewModel.RunSteps.Clear();
        _viewModel.VerificationPlans.Clear();
        _viewModel.VerificationResults.Clear();
        _viewModel.FileChanges.Clear();
        _gitPanelWorkflowService.ClearPanel(_viewModel);
        _verificationPanelWorkflowService.ClearFailure(_viewModel);
        _autoFixWorkflowService.ClearPendingReview();
        _viewModel.AddLog("Side panel cleared");
    }

    private void Exit_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowMaximized();
            return;
        }

        DragMove();
    }

    private void MinimizeWindow_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindow_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowMaximized();
    }

    private void CloseWindow_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowMaximized()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private async Task<DesktopVerificationWorkflowResult?> RunVerificationPlanAsync(AgentVerificationPlan plan)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return null;
        }

        _viewModel.IsBusy = true;
        var operationCts = new CancellationTokenSource();
        _agentRunWorkflowService.SetActiveOperation(operationCts);

        try
        {
            return await _verificationPanelWorkflowService.RunVerificationAsync(
                _viewModel,
                plan,
                _workspaceContextWorkflowService.ProjectConfig?.VerificationCommands,
                TimeSpan.FromMinutes(2),
                operationCts.Token);
        }
        finally
        {
            _agentRunWorkflowService.ClearActiveOperation(operationCts);
            operationCts.Dispose();
            _viewModel.IsBusy = false;
        }
    }

    private async void ReviewGitChanges_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        _viewModel.StatusText = "Preparing code review";

        var result = await _gitPanelWorkflowService.PrepareCodeReviewAsync(_viewModel.WorkspaceRoot);
        if (!_gitPanelWorkflowService.ApplyPromptResult(_viewModel, result, TrimForLog))
        {
            return;
        }

        var messageCountBeforeReview = _viewModel.Messages.Count;
        _viewModel.InputText = result.Prompt;
        await SendCurrentMessageAsync();
        _gitPanelWorkflowService.CaptureLastCodeReview(_viewModel, messageCountBeforeReview);
    }

    private async void FixCodeReviewFindings_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        if (string.IsNullOrWhiteSpace(_gitPanelWorkflowService.LastCodeReviewText))
        {
            _viewModel.StatusText = "No code review to fix";
            return;
        }

        var result = await _gitPanelWorkflowService.PrepareCodeReviewFixAsync(_viewModel.WorkspaceRoot);
        if (!_gitPanelWorkflowService.ApplyPromptResult(_viewModel, result, TrimForLog))
        {
            return;
        }

        _viewModel.InputText = result.Prompt;
        _viewModel.CanFixLastCodeReviewFindings = false;
        await SendCurrentMessageAsync();
    }

    private async void CommitSummary_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        _viewModel.StatusText = "Preparing commit summary";

        var result = await _gitPanelWorkflowService.PrepareCommitSummaryAsync(_viewModel.WorkspaceRoot);
        if (!_gitPanelWorkflowService.ApplyPromptResult(_viewModel, result, TrimForLog))
        {
            return;
        }

        _viewModel.InputText = result.Prompt;
        await SendCurrentMessageAsync();
    }

    private async void CreatePlan_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var planPrompt = _planCheckpointWorkflowService.BuildPlanPrompt(_viewModel);
        if (string.IsNullOrWhiteSpace(planPrompt))
        {
            _viewModel.StatusText = "No goal to plan";
            return;
        }

        _viewModel.InputText = planPrompt;
        _viewModel.AddLog("Plan prompt prepared");
        var messageCountBeforePlan = _viewModel.Messages.Count;
        await SendCurrentMessageAsync();
        _planCheckpointWorkflowService.CapturePlanItems(_viewModel, messageCountBeforePlan);
    }

    private async void ContinuePlanItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ContinueNextPlanItemAsync();
    }

    private async void PlanAndRun_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var planPrompt = _planCheckpointWorkflowService.BuildPlanPrompt(_viewModel);
        if (string.IsNullOrWhiteSpace(planPrompt))
        {
            _viewModel.StatusText = "No goal to plan";
            return;
        }

        _viewModel.InputText = planPrompt;
        _viewModel.AddLog("Plan+run prompt prepared");
        var messageCountBeforePlan = _viewModel.Messages.Count;
        await SendCurrentMessageAsync();
        if (_viewModel.IsBusy)
        {
            return;
        }

        _planCheckpointWorkflowService.CapturePlanItems(_viewModel, messageCountBeforePlan);

        if (_viewModel.PlanItems.Count == 0)
        {
            return;
        }

        await ContinueNextPlanItemAsync();
    }

    private async Task ContinueNextPlanItemAsync()
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        if (_planCheckpointWorkflowService.PrepareNextPlanItem(_viewModel) == null)
        {
            return;
        }

        await SendCurrentMessageAsync();
    }

    private void MarkPlanItemDone_OnClick(object sender, RoutedEventArgs e)
    {
        _planCheckpointWorkflowService.MarkSelectedPlanItemDone(_viewModel);
    }

    private async void MarkDoneAndContinue_OnClick(object sender, RoutedEventArgs e)
    {
        MarkPlanItemDone_OnClick(sender, e);
        if (_viewModel.SelectedPlanItem != null)
        {
            await ContinueNextPlanItemAsync();
        }
    }

    private async void SaveCheckpoint_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _planCheckpointWorkflowService.SaveCheckpointAsync(_viewModel);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Checkpoint save failed: {ex.Message}";
            _viewModel.AddLog($"Checkpoint save failed: {ex.Message}");
        }
    }

    private async void LoadCheckpoint_OnClick(object sender, RoutedEventArgs e)
    {
        await _planCheckpointWorkflowService.LoadLatestCheckpointAsync(_viewModel);
        _viewModel.StatusText = _planCheckpointWorkflowService.HasCheckpoint ? "Checkpoint loaded" : "No checkpoint found";
    }

    private async void ResumeCheckpoint_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var resumePrompt = await _planCheckpointWorkflowService.BuildResumeCheckpointPromptAsync(_viewModel);
        if (string.IsNullOrWhiteSpace(resumePrompt))
        {
            return;
        }

        _viewModel.InputText = resumePrompt;
        await SendCurrentMessageAsync();
    }

    private async void SaveSessionSummary_OnClick(object sender, RoutedEventArgs e)
    {
        await _workspaceContextWorkflowService.SaveSessionSummaryAsync(
            _viewModel,
            "Manual session summary saved",
            TrimForLog);
    }

    private async void LoadSessionSummary_OnClick(object sender, RoutedEventArgs e)
    {
        await _workspaceContextWorkflowService.LoadLatestSessionSummaryAsync(_viewModel);
        _viewModel.StatusText = _workspaceContextWorkflowService.HasSessionSummary
            ? "Session summary loaded"
            : "No session summary found";
    }

    private async void ResumeSessionSummary_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var resumePrompt = await _workspaceContextWorkflowService.BuildResumeSessionSummaryPromptAsync(_viewModel);
        if (string.IsNullOrWhiteSpace(resumePrompt))
        {
            return;
        }

        _viewModel.InputText = resumePrompt;
        await SendCurrentMessageAsync();
    }

    private async Task RefreshGitStatusAsync()
    {
        await _gitPanelWorkflowService.RefreshStatusAsync(_viewModel, TrimForLog);
    }

    private async Task RefreshGitDiffAsync()
    {
        await _gitPanelWorkflowService.RefreshDiffAsync(_viewModel, TrimForLog);
    }

    private void ApproveFileChange_OnClick(object sender, RoutedEventArgs e)
    {
        _fileChangeReviewService.Mark(
            _viewModel,
            (sender as FrameworkElement)?.DataContext as FileChangeRecord,
            FileChangeReviewStatus.Approved);
    }

    private void NeedsEditFileChange_OnClick(object sender, RoutedEventArgs e)
    {
        _fileChangeReviewService.Mark(
            _viewModel,
            (sender as FrameworkElement)?.DataContext as FileChangeRecord,
            FileChangeReviewStatus.NeedsEdit);
    }

    private async void RevertFileChange_OnClick(object sender, RoutedEventArgs e)
    {
        await _fileChangeReviewService.RevertAsync(
            _viewModel,
            (sender as FrameworkElement)?.DataContext as FileChangeRecord);
    }

    private async void ApproveAutoFixAndVerify_OnClick(object sender, RoutedEventArgs e)
    {
        await _autoFixWorkflowService.ApprovePendingChangesAndVerifyAsync(
            _viewModel,
            RunVerificationPlanAsync,
            SendCurrentMessageAsync);
    }

    private async void FixVerificationFailure_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var fixPrompt = _verificationPanelWorkflowService.BuildFixPrompt();
        if (string.IsNullOrWhiteSpace(fixPrompt))
        {
            _viewModel.StatusText = "No failed verification to fix";
            return;
        }

        _viewModel.InputText = fixPrompt;
        await SendCurrentMessageAsync(preserveLastVerificationFailure: true);
    }

    private async void AutoFixVerificationFailure_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        await _autoFixWorkflowService.RunAsync(_viewModel, maxAttempts: 3, SendCurrentMessageAsync);
    }

    private async Task SendCurrentMessageAsync(bool preserveLastVerificationFailure = false)
    {
        await _agentRunWorkflowService.SendCurrentMessageAsync(
            _viewModel,
            _attachments,
            this,
            Dispatcher,
            TrimForLog,
            preserveLastVerificationFailure);
    }

    private void ClearConversation_OnClick(object sender, RoutedEventArgs e)
    {
        _agentRunWorkflowService.ClearConversation(_viewModel);
    }

    private static string TrimForLog(string value)
    {
        value = value.ReplaceLineEndings(" ");
        return value.Length <= 180 ? value : value[..180] + "...";
    }

}
