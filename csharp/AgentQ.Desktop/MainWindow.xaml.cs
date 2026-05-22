using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AgentQ.Desktop.Services;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DesktopStartupCommandService _startupCommandService;
    private readonly DesktopGitPanelWorkflowService _gitPanelWorkflowService;
    private readonly DesktopGitCommandService _gitCommandService;
    private readonly DesktopPlanCommandService _planCommandService;
    private readonly DesktopWorkspaceCommandService _workspaceCommandService;
    private readonly DesktopVerificationCommandService _verificationCommandService;
    private readonly DesktopAgentRunWorkflowService _agentRunWorkflowService;
    private readonly DesktopFileChangeReviewService _fileChangeReviewService;
    private readonly DesktopWindowCommandService _windowCommandService;
    private readonly DesktopPanelEventBinder _panelEventBinder;
    private readonly DesktopProviderModelDiscoveryService _modelDiscoveryService;
    private readonly ProjectMemoryService _projectMemoryService;
    private readonly List<DesktopAttachment> _attachments = [];
    private CancellationTokenSource? _modelRefreshCts;

    public MainWindow(
        MainViewModel viewModel,
        DesktopStartupCommandService startupCommandService,
        DesktopGitPanelWorkflowService gitPanelWorkflowService,
        DesktopGitCommandService gitCommandService,
        DesktopPlanCommandService planCommandService,
        DesktopWorkspaceCommandService workspaceCommandService,
        DesktopVerificationCommandService verificationCommandService,
        DesktopAgentRunWorkflowService agentRunWorkflowService,
        DesktopFileChangeReviewService fileChangeReviewService,
        DesktopWindowCommandService windowCommandService,
        DesktopPanelEventBinder panelEventBinder,
        DesktopProviderModelDiscoveryService modelDiscoveryService,
        ProjectMemoryService projectMemoryService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _startupCommandService = startupCommandService;
        _gitPanelWorkflowService = gitPanelWorkflowService;
        _gitCommandService = gitCommandService;
        _planCommandService = planCommandService;
        _workspaceCommandService = workspaceCommandService;
        _verificationCommandService = verificationCommandService;
        _agentRunWorkflowService = agentRunWorkflowService;
        _fileChangeReviewService = fileChangeReviewService;
        _windowCommandService = windowCommandService;
        _panelEventBinder = panelEventBinder;
        _modelDiscoveryService = modelDiscoveryService;
        _projectMemoryService = projectMemoryService;
        DataContext = _viewModel;
        HookPanelEvents();
        _viewModel.Messages.CollectionChanged += (_, _) => ChatPanelView.ScrollMessagesToEndIfPinned();
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
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
            SaveSettingsAsync = SaveSettingsAndRefreshModelsAsync,
            UpdateApiKey = apiKey => _viewModel.ApiKey = apiKey,
            UpdateEmbeddingApiKey = apiKey => _viewModel.EmbeddingApiKey = apiKey,
            BrowseWorkspaceAsync = () => _workspaceCommandService.BrowseWorkspaceAsync(this, _viewModel, TrimForLog),
            OpenWorkspace = () => _workspaceCommandService.OpenWorkspace(_viewModel),
            RefreshWorkspaceAnalysisAsync = () => _workspaceCommandService.RefreshWorkspaceAnalysisAsync(_viewModel, TrimForLog),
            BuildEmbeddingIndexAsync = () => _workspaceCommandService.BuildEmbeddingIndexAsync(_viewModel, TrimForLog),
            CopyAnalysisReport = () => _workspaceCommandService.CopyWorkspaceAnalysisReport(_viewModel),
            SaveAnalysisReportAsync = () => _workspaceCommandService.SaveWorkspaceAnalysisReportAsync(_viewModel, TrimForLog),
            SaveProjectConfigAsync = () => _workspaceCommandService.SaveProjectConfigAsync(_viewModel, TrimForLog),
            LoadProjectConfigAsync = () => _workspaceCommandService.LoadProjectConfigAsync(_viewModel),
            RunVerificationPlanAsync = plan => _verificationCommandService.RunVerificationPlanAsync(_viewModel, plan),
            FixVerificationFailureAsync = () => _verificationCommandService.FixLastFailureAsync(_viewModel, SendCurrentMessageAsync),
            AutoFixVerificationFailureAsync = () => _verificationCommandService.AutoFixLastFailureAsync(_viewModel, SendCurrentMessageAsync),
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
            SaveSelectedMemoryLessonAsync = SaveSelectedMemoryLessonAsync,
            DismissSelectedMemoryLesson = DismissSelectedMemoryLesson,
            RefreshSavedMemoryAsync = RefreshSavedMemoryAsync,
            DisableSavedMemoryAsync = DisableSavedMemoryAsync,
            DeleteSavedMemoryAsync = DeleteSavedMemoryAsync,
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
                _verificationCommandService.ApprovePendingChangesAndVerifyAsync(_viewModel, SendCurrentMessageAsync),
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
        var result = await _startupCommandService.InitializeAsync(_viewModel, TrimForLog);
        SettingsPanelView.ApiKey = result.ApiKey;
        SettingsPanelView.EmbeddingApiKey = _viewModel.EmbeddingApiKey;
        await RefreshSavedMemoryAsync();
        ScheduleProviderModelRefresh(preserveCurrentModel: true);
    }

    private async void Send_OnClick(object sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void SaveSettings_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveSettingsAndRefreshModelsAsync();
    }

    private async Task SaveSettingsAndRefreshModelsAsync()
    {
        await _workspaceCommandService.SaveSettingsAsync(_viewModel);
        await RefreshProviderModelsAsync(preserveCurrentModel: true, CancellationToken.None);
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Provider) or nameof(MainViewModel.BaseUrl) or nameof(MainViewModel.ApiKey))
        {
            ScheduleProviderModelRefresh(preserveCurrentModel: true);
        }
    }

    private void ScheduleProviderModelRefresh(bool preserveCurrentModel)
    {
        _modelRefreshCts?.Cancel();
        _modelRefreshCts?.Dispose();
        var cts = new CancellationTokenSource();
        _modelRefreshCts = cts;

        _ = RefreshProviderModelsAfterDelayAsync(preserveCurrentModel, cts.Token);
    }

    private async Task RefreshProviderModelsAfterDelayAsync(bool preserveCurrentModel, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(450, cancellationToken);
            await RefreshProviderModelsAsync(preserveCurrentModel, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshProviderModelsAsync(bool preserveCurrentModel, CancellationToken cancellationToken)
    {
        var models = await _modelDiscoveryService.GetModelsAsync(_viewModel.ToConfiguration(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _viewModel.ApplyProviderModels(models, preserveCurrentModel);

        if (!string.IsNullOrWhiteSpace(_viewModel.ApiKey))
        {
            _viewModel.AddLog($"Model list refreshed for {_viewModel.Provider}.");
        }
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
        _windowCommandService.Close(this);
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

    private async Task SaveSelectedMemoryLessonAsync(ProjectMemoryLesson? lesson)
    {
        if (lesson == null)
        {
            _viewModel.StatusText = "No learning candidate selected";
            return;
        }

        await _projectMemoryService.AddLocalLessonAsync(_viewModel.WorkspaceRoot, lesson, CancellationToken.None);
        DismissSelectedMemoryLesson(lesson);
        await RefreshSavedMemoryAsync();
        _viewModel.StatusText = "Learning saved to local memory";
        _viewModel.AddLog($"Learning saved: {lesson.Title}");
    }

    private async Task RefreshSavedMemoryAsync()
    {
        var lessons = await _projectMemoryService.LoadLocalLessonsAsync(_viewModel.WorkspaceRoot, CancellationToken.None);
        _viewModel.SavedMemoryLessons.Clear();
        foreach (var lesson in lessons)
        {
            _viewModel.SavedMemoryLessons.Add(lesson);
        }

        _viewModel.SelectedSavedMemoryLesson = _viewModel.SavedMemoryLessons.FirstOrDefault();
        _viewModel.StatusText = $"Loaded {lessons.Count} local memory lesson(s)";
    }

    private async Task DisableSavedMemoryAsync(ProjectMemoryLesson? lesson)
    {
        if (lesson == null || string.IsNullOrWhiteSpace(lesson.Id))
        {
            _viewModel.StatusText = "No saved memory selected";
            return;
        }

        if (await _projectMemoryService.DisableLocalLessonAsync(_viewModel.WorkspaceRoot, lesson.Id, CancellationToken.None))
        {
            await RefreshSavedMemoryAsync();
            _viewModel.StatusText = "Saved memory disabled";
            _viewModel.AddLog($"Memory disabled: {lesson.Title}");
            return;
        }

        _viewModel.StatusText = "Saved memory was not found";
    }

    private async Task DeleteSavedMemoryAsync(ProjectMemoryLesson? lesson)
    {
        if (lesson == null || string.IsNullOrWhiteSpace(lesson.Id))
        {
            _viewModel.StatusText = "No saved memory selected";
            return;
        }

        if (await _projectMemoryService.DeleteLocalLessonAsync(_viewModel.WorkspaceRoot, lesson.Id, CancellationToken.None))
        {
            await RefreshSavedMemoryAsync();
            _viewModel.StatusText = "Saved memory deleted";
            _viewModel.AddLog($"Memory deleted: {lesson.Title}");
            return;
        }

        _viewModel.StatusText = "Saved memory was not found";
    }

    private void DismissSelectedMemoryLesson(ProjectMemoryLesson? lesson)
    {
        if (lesson == null)
        {
            _viewModel.StatusText = "No learning candidate selected";
            return;
        }

        _viewModel.PendingMemoryLessons.Remove(lesson);
        _viewModel.SelectedPendingMemoryLesson = _viewModel.PendingMemoryLessons.FirstOrDefault();
        _viewModel.StatusText = "Learning candidate dismissed";
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
