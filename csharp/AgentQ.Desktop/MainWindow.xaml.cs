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
    private readonly DesktopPlanCommandService _planCommandService;
    private readonly DesktopWorkspaceContextWorkflowService _workspaceContextWorkflowService;
    private readonly DesktopAgentRunWorkflowService _agentRunWorkflowService;
    private readonly DesktopFileChangeReviewService _fileChangeReviewService;
    private readonly DesktopAttachmentSelectionService _attachmentSelectionService;
    private readonly DesktopClipboardService _clipboardService;
    private readonly DesktopAutoFixWorkflowService _autoFixWorkflowService;
    private readonly DesktopPanelEventBinder _panelEventBinder;
    private readonly List<DesktopAttachment> _attachments = [];

    public MainWindow(
        MainViewModel viewModel,
        DesktopConfigService configService,
        DesktopVerificationPanelWorkflowService verificationPanelWorkflowService,
        DesktopGitPanelWorkflowService gitPanelWorkflowService,
        DesktopPlanCommandService planCommandService,
        DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService,
        DesktopAgentRunWorkflowService agentRunWorkflowService,
        DesktopFileChangeReviewService fileChangeReviewService,
        DesktopAttachmentSelectionService attachmentSelectionService,
        DesktopClipboardService clipboardService,
        DesktopAutoFixWorkflowService autoFixWorkflowService,
        DesktopPanelEventBinder panelEventBinder)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _configService = configService;
        _verificationPanelWorkflowService = verificationPanelWorkflowService;
        _gitPanelWorkflowService = gitPanelWorkflowService;
        _planCommandService = planCommandService;
        _workspaceContextWorkflowService = workspaceContextWorkflowService;
        _agentRunWorkflowService = agentRunWorkflowService;
        _fileChangeReviewService = fileChangeReviewService;
        _attachmentSelectionService = attachmentSelectionService;
        _clipboardService = clipboardService;
        _autoFixWorkflowService = autoFixWorkflowService;
        _panelEventBinder = panelEventBinder;
        DataContext = _viewModel;
        HookPanelEvents();
        _viewModel.Messages.CollectionChanged += (_, _) => ChatPanelView.ScrollMessagesToEndIfPinned();
        Loaded += MainWindow_OnLoaded;
    }

    private void HookPanelEvents()
    {
        _panelEventBinder.Bind(
            SettingsPanelView,
            ProjectPanelView,
            VerificationPanelView,
            PlanPanelView,
            MemoryPanelView,
            ChatPanelView,
            FileChangeReviewPanelView,
            GitPanelView,
            CreatePanelEventCallbacks());
    }

    private DesktopPanelEventCallbacks CreatePanelEventCallbacks()
    {
        return new DesktopPanelEventCallbacks
        {
            SaveSettings = () => SaveSettings_OnClick(this, new RoutedEventArgs()),
            UpdateApiKey = apiKey => _viewModel.ApiKey = apiKey,
            BrowseWorkspace = () => BrowseWorkspace_OnClick(this, new RoutedEventArgs()),
            OpenWorkspace = () => OpenWorkspace_OnClick(this, new RoutedEventArgs()),
            RefreshWorkspaceAnalysis = () => RefreshWorkspaceAnalysis_OnClick(this, new RoutedEventArgs()),
            SaveProjectConfig = () => SaveProjectConfig_OnClick(this, new RoutedEventArgs()),
            LoadProjectConfig = () => LoadProjectConfig_OnClick(this, new RoutedEventArgs()),
            RunVerificationPlanAsync = RunVerificationPlanAsync,
            FixVerificationFailure = () => FixVerificationFailure_OnClick(this, new RoutedEventArgs()),
            AutoFixVerificationFailure = () => AutoFixVerificationFailure_OnClick(this, new RoutedEventArgs()),
            CreatePlan = () => CreatePlan_OnClick(this, new RoutedEventArgs()),
            ContinuePlanItem = () => ContinuePlanItem_OnClick(this, new RoutedEventArgs()),
            MarkPlanItemDone = () => MarkPlanItemDone_OnClick(this, new RoutedEventArgs()),
            SaveCheckpoint = () => SaveCheckpoint_OnClick(this, new RoutedEventArgs()),
            LoadCheckpoint = () => LoadCheckpoint_OnClick(this, new RoutedEventArgs()),
            ResumeCheckpoint = () => ResumeCheckpoint_OnClick(this, new RoutedEventArgs()),
            PlanAndRun = () => PlanAndRun_OnClick(this, new RoutedEventArgs()),
            MarkDoneAndContinue = () => MarkDoneAndContinue_OnClick(this, new RoutedEventArgs()),
            SaveSessionSummary = () => SaveSessionSummary_OnClick(this, new RoutedEventArgs()),
            LoadSessionSummary = () => LoadSessionSummary_OnClick(this, new RoutedEventArgs()),
            ResumeSessionSummary = () => ResumeSessionSummary_OnClick(this, new RoutedEventArgs()),
            AttachFiles = () => AttachFiles_OnClick(this, new RoutedEventArgs()),
            ClearAttachments = () => ClearAttachments_OnClick(this, new RoutedEventArgs()),
            SendCurrentMessageAsync = () => SendCurrentMessageAsync(),
            ContinueLastRun = () => ContinueLastRun_OnClick(this, new RoutedEventArgs()),
            StopAgent = () => StopAgent_OnClick(this, new RoutedEventArgs()),
            CopyMessage = message => _clipboardService.CopyMessage(_viewModel, message),
            ApproveFileChange = record => _fileChangeReviewService.Mark(_viewModel, record, FileChangeReviewStatus.Approved),
            MarkFileChangeNeedsEdit = record => _fileChangeReviewService.Mark(_viewModel, record, FileChangeReviewStatus.NeedsEdit),
            RevertFileChangeAsync = record => _fileChangeReviewService.RevertAsync(_viewModel, record),
            ApproveAllFileChangesAndVerifyAsync = () =>
                _autoFixWorkflowService.ApprovePendingChangesAndVerifyAsync(_viewModel, RunVerificationPlanAsync, SendCurrentMessageAsync),
            RefreshGitStatusAsync = RefreshGitStatusAsync,
            RefreshGitDiffAsync = RefreshGitDiffAsync,
            ReviewGitChanges = () => ReviewGitChanges_OnClick(this, new RoutedEventArgs()),
            FixCodeReviewFindings = () => FixCodeReviewFindings_OnClick(this, new RoutedEventArgs()),
            CommitSummary = () => CommitSummary_OnClick(this, new RoutedEventArgs()),
            PullFastForwardAsync = () => _gitPanelWorkflowService.PullFastForwardOnlyAsync(_viewModel, TrimForLog),
            CreateBackupBranchAsync = () => _gitPanelWorkflowService.CreateBackupBranchAsync(_viewModel, TrimForLog),
            CheckoutMainAsync = () => _gitPanelWorkflowService.CheckoutMainAsync(_viewModel, TrimForLog),
            LoadSelectedGitFileDiffAsync = () => _gitPanelWorkflowService.LoadSelectedFileDiffAsync(_viewModel),
            ApproveGitChange = () => _gitPanelWorkflowService.SetSelectedReviewStatus(_viewModel, GitChangeReviewStatus.Approved),
            RejectGitChange = () => _gitPanelWorkflowService.SetSelectedReviewStatus(_viewModel, GitChangeReviewStatus.Rejected),
            MarkGitChangeNeedsEdit = () => _gitPanelWorkflowService.SetSelectedReviewStatus(_viewModel, GitChangeReviewStatus.NeedsEdit),
            StageSelectedGitFileAsync = () => _gitPanelWorkflowService.StageSelectedFileAsync(_viewModel, TrimForLog),
            StageApprovedGitFilesAsync = () => _gitPanelWorkflowService.StageApprovedFilesAsync(_viewModel, TrimForLog),
            UnstageSelectedGitFileAsync = () => _gitPanelWorkflowService.UnstageSelectedFileAsync(_viewModel, TrimForLog),
            CommitStagedGitFilesAsync = () => _gitPanelWorkflowService.CommitAsync(_viewModel, TrimForLog)
        };
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
        await _planCommandService.CreatePlanAsync(_viewModel, SendCurrentMessageAsync);
    }

    private async void ContinuePlanItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ContinueNextPlanItemAsync();
    }

    private async void PlanAndRun_OnClick(object sender, RoutedEventArgs e)
    {
        await _planCommandService.PlanAndRunAsync(_viewModel, SendCurrentMessageAsync);
    }

    private async Task ContinueNextPlanItemAsync()
    {
        await _planCommandService.ContinueNextPlanItemAsync(_viewModel, SendCurrentMessageAsync);
    }

    private void MarkPlanItemDone_OnClick(object sender, RoutedEventArgs e)
    {
        _planCommandService.MarkPlanItemDone(_viewModel);
    }

    private async void MarkDoneAndContinue_OnClick(object sender, RoutedEventArgs e)
    {
        await _planCommandService.MarkDoneAndContinueAsync(_viewModel, SendCurrentMessageAsync);
    }

    private async void SaveCheckpoint_OnClick(object sender, RoutedEventArgs e)
    {
        await _planCommandService.SaveCheckpointAsync(_viewModel);
    }

    private async void LoadCheckpoint_OnClick(object sender, RoutedEventArgs e)
    {
        await _planCommandService.LoadCheckpointAsync(_viewModel);
    }

    private async void ResumeCheckpoint_OnClick(object sender, RoutedEventArgs e)
    {
        await _planCommandService.ResumeCheckpointAsync(_viewModel, SendCurrentMessageAsync);
    }

    private async void SaveSessionSummary_OnClick(object sender, RoutedEventArgs e)
    {
        await _planCommandService.SaveSessionSummaryAsync(_viewModel, TrimForLog);
    }

    private async void LoadSessionSummary_OnClick(object sender, RoutedEventArgs e)
    {
        await _planCommandService.LoadSessionSummaryAsync(_viewModel);
    }

    private async void ResumeSessionSummary_OnClick(object sender, RoutedEventArgs e)
    {
        await _planCommandService.ResumeSessionSummaryAsync(_viewModel, SendCurrentMessageAsync);
    }

    private async Task RefreshGitStatusAsync()
    {
        await _gitPanelWorkflowService.RefreshStatusAsync(_viewModel, TrimForLog);
    }

    private async Task RefreshGitDiffAsync()
    {
        await _gitPanelWorkflowService.RefreshDiffAsync(_viewModel, TrimForLog);
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
