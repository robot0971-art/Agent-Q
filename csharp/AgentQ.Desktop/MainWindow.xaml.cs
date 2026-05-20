using System.Linq;
using System.Windows;
using System.Windows.Input;
using AgentQ.Core.Providers;
using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DesktopConfigService _configService;
    private readonly DesktopVerificationPanelWorkflowService _verificationPanelWorkflowService;
    private readonly DesktopGitPanelWorkflowService _gitPanelWorkflowService;
    private readonly DesktopGitCommandService _gitCommandService;
    private readonly DesktopPlanCommandService _planCommandService;
    private readonly DesktopWorkspaceCommandService _workspaceCommandService;
    private readonly DesktopWorkspaceContextWorkflowService _workspaceContextWorkflowService;
    private readonly DesktopAgentRunWorkflowService _agentRunWorkflowService;
    private readonly DesktopFileChangeReviewService _fileChangeReviewService;
    private readonly DesktopAutoFixWorkflowService _autoFixWorkflowService;
    private readonly DesktopWindowCommandService _windowCommandService;
    private readonly DesktopPanelEventBinder _panelEventBinder;
    private readonly List<DesktopAttachment> _attachments = [];

    public MainWindow(
        MainViewModel viewModel,
        DesktopConfigService configService,
        DesktopVerificationPanelWorkflowService verificationPanelWorkflowService,
        DesktopGitPanelWorkflowService gitPanelWorkflowService,
        DesktopGitCommandService gitCommandService,
        DesktopPlanCommandService planCommandService,
        DesktopWorkspaceCommandService workspaceCommandService,
        DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService,
        DesktopAgentRunWorkflowService agentRunWorkflowService,
        DesktopFileChangeReviewService fileChangeReviewService,
        DesktopAutoFixWorkflowService autoFixWorkflowService,
        DesktopWindowCommandService windowCommandService,
        DesktopPanelEventBinder panelEventBinder)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _configService = configService;
        _verificationPanelWorkflowService = verificationPanelWorkflowService;
        _gitPanelWorkflowService = gitPanelWorkflowService;
        _gitCommandService = gitCommandService;
        _planCommandService = planCommandService;
        _workspaceCommandService = workspaceCommandService;
        _workspaceContextWorkflowService = workspaceContextWorkflowService;
        _agentRunWorkflowService = agentRunWorkflowService;
        _fileChangeReviewService = fileChangeReviewService;
        _autoFixWorkflowService = autoFixWorkflowService;
        _windowCommandService = windowCommandService;
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
            SaveSettingsAsync = () => _workspaceCommandService.SaveSettingsAsync(_viewModel),
            UpdateApiKey = apiKey => _viewModel.ApiKey = apiKey,
            BrowseWorkspaceAsync = () => _workspaceCommandService.BrowseWorkspaceAsync(this, _viewModel, TrimForLog),
            OpenWorkspace = () => _workspaceCommandService.OpenWorkspace(_viewModel),
            RefreshWorkspaceAnalysisAsync = () => _workspaceCommandService.RefreshWorkspaceAnalysisAsync(_viewModel, TrimForLog),
            SaveProjectConfigAsync = () => _workspaceCommandService.SaveProjectConfigAsync(_viewModel, TrimForLog),
            LoadProjectConfigAsync = () => _workspaceCommandService.LoadProjectConfigAsync(_viewModel),
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
            AttachFiles = () => _workspaceCommandService.SelectAttachments(this, _viewModel, _attachments),
            ClearAttachments = () => _workspaceCommandService.ClearAttachments(_viewModel, _attachments),
            SendCurrentMessageAsync = () => SendCurrentMessageAsync(),
            ContinueLastRun = () => ContinueLastRun_OnClick(this, new RoutedEventArgs()),
            StopAgent = () => StopAgent_OnClick(this, new RoutedEventArgs()),
            CopyMessage = message => _workspaceCommandService.CopyMessage(_viewModel, message),
            ApproveFileChange = record => _fileChangeReviewService.Mark(_viewModel, record, FileChangeReviewStatus.Approved),
            MarkFileChangeNeedsEdit = record => _fileChangeReviewService.Mark(_viewModel, record, FileChangeReviewStatus.NeedsEdit),
            RevertFileChangeAsync = record => _fileChangeReviewService.RevertAsync(_viewModel, record),
            ApproveAllFileChangesAndVerifyAsync = () =>
                _autoFixWorkflowService.ApprovePendingChangesAndVerifyAsync(_viewModel, RunVerificationPlanAsync, SendCurrentMessageAsync),
            RefreshGitStatusAsync = () => _gitCommandService.RefreshStatusAsync(_viewModel, TrimForLog),
            RefreshGitDiffAsync = () => _gitCommandService.RefreshDiffAsync(_viewModel, TrimForLog),
            ReviewGitChangesAsync = () => _gitCommandService.ReviewChangesAsync(_viewModel, SendCurrentMessageAsync, TrimForLog),
            FixCodeReviewFindingsAsync = () => _gitCommandService.FixCodeReviewFindingsAsync(_viewModel, SendCurrentMessageAsync, TrimForLog),
            CommitSummaryAsync = () => _gitCommandService.PrepareCommitSummaryAsync(_viewModel, SendCurrentMessageAsync, TrimForLog),
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

    private async void Send_OnClick(object sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void SaveSettings_OnClick(object sender, RoutedEventArgs e)
    {
        await _workspaceCommandService.SaveSettingsAsync(_viewModel);
    }

    private async void BrowseWorkspace_OnClick(object sender, RoutedEventArgs e)
    {
        await _workspaceCommandService.BrowseWorkspaceAsync(this, _viewModel, TrimForLog);
    }

    private void AttachFiles_OnClick(object sender, RoutedEventArgs e)
    {
        _workspaceCommandService.SelectAttachments(this, _viewModel, _attachments);
    }

    private void ClearAttachments_OnClick(object sender, RoutedEventArgs e)
    {
        _workspaceCommandService.ClearAttachments(_viewModel, _attachments);
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
        _windowCommandService.IncreaseFontSize(_viewModel);
    }

    private void DecreaseFontSize_OnClick(object sender, RoutedEventArgs e)
    {
        _windowCommandService.DecreaseFontSize(_viewModel);
    }

    private void ResetFontSize_OnClick(object sender, RoutedEventArgs e)
    {
        _windowCommandService.ResetFontSize(_viewModel);
    }

    private void ShowStatus_OnClick(object sender, RoutedEventArgs e)
    {
        _windowCommandService.ShowStatus(_viewModel);
    }

    private void CopyLastAssistantMessage_OnClick(object sender, RoutedEventArgs e)
    {
        _workspaceCommandService.CopyLastAssistantMessage(_viewModel);
    }

    private void CopyConversation_OnClick(object sender, RoutedEventArgs e)
    {
        _workspaceCommandService.CopyConversation(_viewModel);
    }

    private void SmoothScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _windowCommandService.HandleSmoothScroll(sender, e);
    }

    private void ClearLogs_OnClick(object sender, RoutedEventArgs e)
    {
        _windowCommandService.ClearLogs(_viewModel);
    }

    private void ClearSidePanel_OnClick(object sender, RoutedEventArgs e)
    {
        _windowCommandService.ClearSidePanel(_viewModel);
    }

    private void Exit_OnClick(object sender, RoutedEventArgs e)
    {
        _windowCommandService.Exit(this);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _windowCommandService.HandleTitleBarMouseDown(this, e);
    }

    private void MinimizeWindow_OnClick(object sender, RoutedEventArgs e)
    {
        _windowCommandService.Minimize(this);
    }

    private void MaximizeWindow_OnClick(object sender, RoutedEventArgs e)
    {
        _windowCommandService.ToggleWindowMaximized(this);
    }

    private void CloseWindow_OnClick(object sender, RoutedEventArgs e)
    {
        _windowCommandService.Close(this);
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
