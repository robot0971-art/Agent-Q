using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

    private static readonly string[] SupportedAttachmentExtensions =
    [
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif",
        ".mp4", ".mov", ".avi", ".mkv", ".webm"
    ];

    private readonly MainViewModel _viewModel = new();
    private readonly DesktopConfigService _configService = new();
    private readonly DesktopAgentService _agentService = new();
    private readonly DesktopVerificationRunner _verificationRunner = new();
    private readonly DesktopVerificationPanelWorkflowService _verificationPanelWorkflowService;
    private readonly DesktopGitService _gitService = new();
    private readonly DesktopGitPanelWorkflowService _gitPanelWorkflowService;
    private readonly WorkspaceAnalysisService _workspaceAnalysisService = new();
    private readonly ProjectAgentConfigService _projectConfigService = new();
    private readonly AgentCheckpointService _checkpointService = new();
    private readonly DesktopPlanWorkflowService _planWorkflowService = new();
    private readonly DesktopPlanCheckpointWorkflowService _planCheckpointWorkflowService;
    private readonly AgentSessionSummaryService _sessionSummaryService = new();
    private readonly DesktopWorkspaceContextWorkflowService _workspaceContextWorkflowService;
    private readonly DesktopAgentRunWorkflowService _agentRunWorkflowService;
    private readonly DesktopFileChangeReviewService _fileChangeReviewService = new();
    private readonly DesktopCheckpointWorkflowService _checkpointWorkflowService;
    private readonly VerificationFailureClassifier _verificationFailureClassifier = new();
    private readonly List<DesktopAttachment> _attachments = [];
    private readonly List<FileChangeRecord> _pendingAutoFixChanges = [];
    private AgentVerificationPlan? _pendingAutoFixVerificationPlan;
    private int _pendingAutoFixNextAttempt;
    private int _pendingAutoFixMaxAttempts;
    private string _pendingAutoFixPreviousFailureSignature = string.Empty;
    private bool _messagesPinnedToBottom = true;

    public MainWindow()
    {
        InitializeComponent();
        _verificationPanelWorkflowService = new DesktopVerificationPanelWorkflowService(
            new DesktopVerificationWorkflowService(
                _verificationRunner,
                _verificationFailureClassifier));
        _gitPanelWorkflowService = new DesktopGitPanelWorkflowService(_gitService);
        _checkpointWorkflowService = new DesktopCheckpointWorkflowService(_checkpointService, _gitService);
        _planCheckpointWorkflowService = new DesktopPlanCheckpointWorkflowService(
            _planWorkflowService,
            _checkpointWorkflowService);
        _workspaceContextWorkflowService = new DesktopWorkspaceContextWorkflowService(
            _workspaceAnalysisService,
            _projectConfigService,
            _sessionSummaryService,
            _planCheckpointWorkflowService);
        _agentRunWorkflowService = new DesktopAgentRunWorkflowService(
            _agentService,
            _workspaceContextWorkflowService,
            _verificationPanelWorkflowService);
        DataContext = _viewModel;
        _viewModel.Messages.CollectionChanged += (_, _) => ScrollMessagesToEndIfPinned();
        Loaded += MainWindow_OnLoaded;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var saved = await _configService.LoadAsync();
        if (saved != null)
        {
            _viewModel.ApplyConfiguration(saved);
            ApiKeyBox.Password = saved.ApiKey;
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
            _viewModel.StatusText = "Enter an API key and save settings.";
        }

        _viewModel.AddLog("AgentQ Desktop started");
        await _workspaceContextWorkflowService.LoadWorkspaceContextAsync(_viewModel, TrimForLog);
    }

    private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.ApiKey = ApiKeyBox.Password;
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
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select images or videos",
            Multiselect = true,
            Filter = "Images/Videos|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.mp4;*.mov;*.avi;*.mkv;*.webm|All files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!SupportedAttachmentExtensions.Contains(extension))
            {
                _viewModel.AddLog($"Unsupported attachment type: {Path.GetFileName(path)}");
                continue;
            }

            if (_attachments.Any(attachment => string.Equals(attachment.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _attachments.Add(new DesktopAttachment
            {
                Path = path,
                FileName = Path.GetFileName(path),
                MediaType = GetMediaType(extension)
            });
            _viewModel.Attachments.Add(Path.GetFileName(path));
        }

        _viewModel.StatusText = _attachments.Count == 0
            ? "No attachments selected."
            : $"{_attachments.Count} attachment(s) selected.";
    }

    private void ClearAttachments_OnClick(object sender, RoutedEventArgs e)
    {
        _attachments.Clear();
        _viewModel.Attachments.Clear();
        _viewModel.StatusText = "Attachments cleared";
    }

    private void InputBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        e.Handled = true;
        _ = SendCurrentMessageAsync();
    }

    private async void Send_OnClick(object sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
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

    private void CopyMessage_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ChatMessageViewModel message } ||
            string.IsNullOrEmpty(message.Content))
        {
            return;
        }

        System.Windows.Clipboard.SetText(message.Content);
        _viewModel.StatusText = "Message copied to clipboard";
    }

    private void CopyLastAssistantMessage_OnClick(object sender, RoutedEventArgs e)
    {
        var message = _viewModel.Messages.LastOrDefault(item =>
            string.Equals(item.Role, "AgentQ", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.Content));

        if (message == null)
        {
            _viewModel.StatusText = "No AgentQ response to copy";
            return;
        }

        System.Windows.Clipboard.SetText(message.Content);
        _viewModel.StatusText = "Last response copied to clipboard";
    }

    private void CopyConversation_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Messages.Count == 0)
        {
            _viewModel.StatusText = "No conversation to copy";
            return;
        }

        var builder = new StringBuilder();
        foreach (var message in _viewModel.Messages)
        {
            builder.AppendLine($"{message.Role}:");
            builder.AppendLine(message.Content);
            builder.AppendLine();
        }

        System.Windows.Clipboard.SetText(builder.ToString().TrimEnd());
        _viewModel.StatusText = "Conversation copied to clipboard";
    }

    private void MessageTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            textBox.ScrollToEnd();
        }
    }

    private void MessageTextBox_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = FindDescendant<ScrollViewer>(MessagesList);
        if (scrollViewer == null)
        {
            return;
        }

        e.Handled = true;
        SmoothScroll(scrollViewer, e.Delta);
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

    private void ScrollMessagesToEndIfPinned()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var scrollViewer = FindDescendant<ScrollViewer>(MessagesList);
            if (scrollViewer == null || !_messagesPinnedToBottom)
            {
                return;
            }

            scrollViewer.ScrollToEnd();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static bool IsNearBottom(ScrollViewer scrollViewer)
    {
        return scrollViewer.ScrollableHeight <= 0 ||
               scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset < 80;
    }

    private void MessagesList_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is ScrollViewer scrollViewer)
        {
            _messagesPinnedToBottom = IsNearBottom(scrollViewer);
        }
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
        ClearPendingAutoFixReview();
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

    private async void RunVerification_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: AgentVerificationPlan plan } ||
            string.IsNullOrWhiteSpace(plan.Command))
        {
            return;
        }

        await RunVerificationPlanAsync(plan);
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

    private async void RefreshGitStatus_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshGitStatusAsync();
    }

    private async void RefreshGitDiff_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshGitDiffAsync();
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

    private async void GitChangedFiles_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await _gitPanelWorkflowService.LoadSelectedFileDiffAsync(_viewModel);
    }

    private void ApproveGitChange_OnClick(object sender, RoutedEventArgs e)
    {
        _gitPanelWorkflowService.SetSelectedReviewStatus(_viewModel, GitChangeReviewStatus.Approved);
    }

    private void RejectGitChange_OnClick(object sender, RoutedEventArgs e)
    {
        _gitPanelWorkflowService.SetSelectedReviewStatus(_viewModel, GitChangeReviewStatus.Rejected);
    }

    private void NeedsEditGitChange_OnClick(object sender, RoutedEventArgs e)
    {
        _gitPanelWorkflowService.SetSelectedReviewStatus(_viewModel, GitChangeReviewStatus.NeedsEdit);
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
        await ApprovePendingAutoFixChangesAndVerifyAsync();
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

        await RunAutoFixVerificationLoopAsync(maxAttempts: 3);
    }

    private Task RunAutoFixVerificationLoopAsync(int maxAttempts)
    {
        return RunAutoFixVerificationLoopAsync(
            maxAttempts,
            startAttempt: 1,
            previousFailureSignature: _verificationPanelWorkflowService.LastFailureSignature);
    }

    private async Task RunAutoFixVerificationLoopAsync(
        int maxAttempts,
        int startAttempt,
        string previousFailureSignature)
    {
        if (startAttempt > maxAttempts)
        {
            _viewModel.AddRunStep(
                AgentRunState.Failed,
                "Auto fix stopped: max attempts reached",
                $"Tried {maxAttempts} fix attempts.");
            _viewModel.StatusText = $"Auto fix stopped after {maxAttempts} attempts";
            return;
        }

        var retryPlan = _verificationPanelWorkflowService.CreateRetryPlan();
        var fixPrompt = _verificationPanelWorkflowService.BuildFixPrompt();
        if (retryPlan == null || string.IsNullOrWhiteSpace(fixPrompt))
        {
            _viewModel.StatusText = startAttempt == 1
                ? "No failed verification to auto-fix"
                : "Auto fix stopped: no failed verification remains";
            return;
        }

        var fileChangeCountBeforeAttempt = _viewModel.FileChanges.Count;
        var workspaceFingerprintBeforeAttempt = await BuildWorkspaceChangeFingerprintAsync();

        _viewModel.AddRunStep(
            AgentRunState.Planning,
            $"Auto fix attempt {startAttempt}/{maxAttempts}",
            $"Fix, then rerun: {retryPlan.Command}");
        _viewModel.InputText = fixPrompt;
        await SendCurrentMessageAsync(preserveLastVerificationFailure: true);

        if (_viewModel.IsBusy)
        {
            return;
        }

        var recordedFileChangeCount = _viewModel.FileChanges.Count - fileChangeCountBeforeAttempt;
        var workspaceFingerprintAfterAttempt = await BuildWorkspaceChangeFingerprintAsync();
        if (recordedFileChangeCount <= 0 &&
            string.Equals(workspaceFingerprintBeforeAttempt, workspaceFingerprintAfterAttempt, StringComparison.Ordinal))
        {
            _viewModel.AddRunStep(
                AgentRunState.Failed,
                "Auto fix stopped: no file changes",
                "The fix attempt did not change the workspace.");
            _viewModel.StatusText = "Auto fix stopped: no file changes";
            return;
        }

        _viewModel.AddRunStep(
            AgentRunState.RecordingChanges,
            "Auto fix changes detected",
            recordedFileChangeCount > 0
                ? $"{recordedFileChangeCount} file change(s) recorded."
                : "Workspace diff changed.");

        PauseAutoFixForReview(
            retryPlan,
            _viewModel.FileChanges.Skip(fileChangeCountBeforeAttempt).ToList(),
            startAttempt + 1,
            maxAttempts,
            previousFailureSignature);
    }

    private void PauseAutoFixForReview(
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

        _viewModel.AddRunStep(
            AgentRunState.WaitingForApproval,
            "Auto fix paused for review",
            "Review the changed files in Preview, then choose Approve all & verify.");
        _viewModel.StatusText = "Review Auto Fix changes before verification";
    }

    private async Task ApprovePendingAutoFixChangesAndVerifyAsync()
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.StatusText = "AgentQ is busy";
            return;
        }

        if (_pendingAutoFixVerificationPlan == null)
        {
            _viewModel.StatusText = "No pending Auto Fix verification";
            return;
        }

        if (_pendingAutoFixChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.NeedsEdit))
        {
            _viewModel.StatusText = "Auto Fix changes need edits before verification";
            _viewModel.AddRunStep(
                AgentRunState.WaitingForApproval,
                "Auto fix waiting for edits",
                "One or more pending changes are marked Needs edit.");
            return;
        }

        if (_pendingAutoFixChanges.Any(change => change.ReviewStatus == FileChangeReviewStatus.Reverted))
        {
            ClearPendingAutoFixReview();
            _viewModel.StatusText = "Auto Fix verification cancelled after revert";
            _viewModel.AddRunStep(
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
        ClearPendingAutoFixReview();

        _viewModel.AddRunStep(
            AgentRunState.Verifying,
            "Approved Auto Fix changes",
            retryPlan.Command);
        var verificationResult = await RunVerificationPlanAsync(retryPlan);

        if (verificationResult?.Succeeded == true)
        {
            _viewModel.AddRunStep(AgentRunState.Done, "Auto fix succeeded", retryPlan.Command);
            _viewModel.StatusText = "Auto fix succeeded";
            return;
        }

        var currentFailureSignature = _verificationPanelWorkflowService.LastFailureSignature;
        if (!string.IsNullOrWhiteSpace(currentFailureSignature) &&
            string.Equals(previousFailureSignature, currentFailureSignature, StringComparison.Ordinal))
        {
            _viewModel.AddRunStep(
                AgentRunState.Failed,
                "Auto fix stopped: repeated failure",
                "The latest verification failed in the same way as before.");
            _viewModel.StatusText = "Auto fix stopped: repeated failure";
            return;
        }

        if (nextAttempt > maxAttempts)
        {
            _viewModel.AddRunStep(
                AgentRunState.Failed,
                "Auto fix stopped: max attempts reached",
                $"Tried {maxAttempts} fix attempts.");
            _viewModel.StatusText = $"Auto fix stopped after {maxAttempts} attempts";
            return;
        }

        await RunAutoFixVerificationLoopAsync(maxAttempts, nextAttempt, currentFailureSignature);
    }

    private void ClearPendingAutoFixReview()
    {
        _pendingAutoFixVerificationPlan = null;
        _pendingAutoFixChanges.Clear();
        _pendingAutoFixNextAttempt = 0;
        _pendingAutoFixMaxAttempts = 0;
        _pendingAutoFixPreviousFailureSignature = string.Empty;
    }

    private async Task<string> BuildWorkspaceChangeFingerprintAsync(CancellationToken ct = default)
    {
        var status = await _gitService.GetStatusAsync(_viewModel.WorkspaceRoot, ct);
        var diff = await _gitService.GetFullDiffAsync(_viewModel.WorkspaceRoot, ct);
        var content = $"{status.ExitCode}\n{status.StandardOutput}\n{status.StandardError}\n---diff---\n{diff.ExitCode}\n{diff.StandardOutput}\n{diff.StandardError}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
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

    private static string GetMediaType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
    }

}
