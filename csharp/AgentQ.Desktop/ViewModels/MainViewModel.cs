using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Core.Providers;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _provider = "opencode-go";
    private string _model = "kimi-k2.6";
    private string _baseUrl = ProviderConfiguration.OpenCodeGoDefaultBaseUrl;
    private string _apiKey = string.Empty;
    private string _embeddingProvider = "openai";
    private string _embeddingModel = DesktopEmbeddingClientFactory.DefaultEmbeddingModel;
    private string _embeddingBaseUrl = "https://api.openai.com/v1";
    private string _embeddingApiKey = string.Empty;
    private string _workspaceRoot = Environment.CurrentDirectory;
    private string _inputText = string.Empty;
    private string _statusText = "Ready";
    private int _timeoutSeconds;
    private uint _maxTokens = 4096;
    private double _desktopFontSize = 14;
    private bool _autoAttachWorkspaceContext = true;
    private bool _autoFetchLinks = true;
    private bool _enableScreenshotLlmVisionReview;
    private string _uiLanguage = "English";
    private AgentWorkMode _workMode = AgentWorkMode.Coding;
    private bool _isBusy;
    private bool _canResumeCheckpoint;
    private bool _canContinueLastRun;
    private string _lastContinuationPrompt = string.Empty;
    private string _latestCheckpointText = "No checkpoint loaded.";
    private string _latestSessionSummaryText = "No session summary saved.";
    private string _projectConfigText = "No project config loaded.";
    private string _sourceFileFilter = string.Empty;
    private string _sourceFilePreviewText = "Select a file to preview its source.";
    private string _reviewWorkflowText = "No auto verification is waiting. Review changes manually or start Auto fix from a failed verification.";
    private string _pendingReviewVerificationText = "No verification queued.";
    private string _memoryGcPreviewText = "Memory cleanup preview not run.";
    private string _planEvidenceSummary = "No plan evidence yet. Create or load a plan, then run an item to connect evidence and verification.";
    private string _planEvidenceStatusText = "No plan selected";
    private string _planEvidenceAccentBrush = "#B7C4D1";
    private string _planApprovalPreviewText = "No plan approval preview.";
    private string _planApprovalStateText = "No approval needed";
    private string _planApprovalAccentBrush = "#B7C4D1";
    private string _usageText = "\uC0AC\uC6A9\uB7C9 \uC815\uBCF4 \uC5C6\uC74C";
    private string _runPermissionStatusText = "Run permissions: none";
    private bool _canClearRunPermissions;
    private bool _hasProjectConfig;
    private bool _canResumeSessionSummary;
    private bool _hasPendingReviewVerification;
    private bool _hasPendingPlanApproval;
    private SourceFileEntry? _selectedSourceFile;
    private FileChangeRecord? _selectedFileChange;
    private AgentPlanItem? _selectedPlanItem;
    private ProjectMemoryLesson? _selectedPendingMemoryLesson;
    private ProjectMemoryLesson? _selectedSavedMemoryLesson;
    private WorkerExecutionContext? _currentWorkerExecutionContext;
    private AgentRunState _currentRunState = AgentRunState.Idle;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        RunSteps.CollectionChanged += (_, _) => RefreshRunSummary();
        FileChanges.CollectionChanged += FileChangesOnCollectionChanged;
        Verification.Results.CollectionChanged += (_, _) =>
        {
            RefreshRunSummary();
            RefreshPlanEvidenceSummary();
        };
        PlanItems.CollectionChanged += (_, _) => RefreshPlanEvidenceSummary();

        Git.PropertyChanged += (_, e) =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Git)));
            if (GetLegacyGitPropertyName(e.PropertyName) is { } propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        };
        Verification.PropertyChanged += (_, e) =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Verification)));
            if (GetLegacyVerificationPropertyName(e.PropertyName) is { } propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        };
        Project.PropertyChanged += (_, e) =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Project)));
            if (GetLegacyProjectPropertyName(e.PropertyName) is { } propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        };
        EvalDashboard.PropertyChanged += (_, e) =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EvalDashboard)));
            if (GetLegacyEvalDashboardPropertyName(e.PropertyName) is { } propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        };

        RefreshRunSummary();
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<string> Logs { get; } = [];

    public ObservableCollection<FileChangeRecord> FileChanges { get; } = [];

    public ObservableCollection<SourceFileEntry> SourceFiles { get; } = [];

    public ObservableCollection<AgentRunStep> RunSteps { get; } = [];

    public VerificationPanelViewModel Verification { get; } = new();

    public EvalDashboardViewModel EvalDashboard { get; } = new();

    public GitPanelViewModel Git { get; } = new();

    public ProjectPanelViewModel Project { get; } = new();

    public RunSummaryViewModel RunSummary { get; } = new();

    public ObservableCollection<string> EvalDashboardMetrics => EvalDashboard.Metrics;

    public ObservableCollection<string> EvalDashboardFindings => EvalDashboard.Findings;

    public ObservableCollection<string> EvalDashboardReplayEntries => EvalDashboard.ReplayEntries;

    public ObservableCollection<string> EvalDashboardFailureFingerprints => EvalDashboard.FailureFingerprints;

    public ObservableCollection<AgentPlanItem> PlanItems { get; } = [];

    public ObservableCollection<string> WorkspaceVerificationCommands => Project.VerificationCommands;

    public ObservableCollection<string> WorkspaceProjectMap => Project.ProjectMap;

    public ObservableCollection<string> WorkspaceKeySymbols => Project.KeySymbols;

    public ObservableCollection<string> WorkspaceKeyDependencies => Project.KeyDependencies;

    public ObservableCollection<string> WorkspaceKeyFiles => Project.KeyFiles;

    public ObservableCollection<string> WorkspaceHints => Project.Hints;

    public ObservableCollection<string> Attachments { get; } = [];

    public ObservableCollection<ProjectMemoryLesson> PendingMemoryLessons { get; } = [];

    public ObservableCollection<ProjectMemoryLesson> SavedMemoryLessons { get; } = [];

    public ObservableCollection<string> AvailableProviders { get; } = new(DesktopProviderModelCatalog.Providers);

    public ObservableCollection<string> AvailableModels { get; } = new(DesktopProviderModelCatalog.GetModels("opencode-go"));

    public ObservableCollection<string> AvailableEmbeddingProviders { get; } = new(["openai", "none", "custom"]);

    public ObservableCollection<AgentWorkMode> AvailableWorkModes { get; } = new(Enum.GetValues<AgentWorkMode>());

    public ObservableCollection<string> AvailableUiLanguages { get; } = new(["English", "\uD55C\uAD6D\uC5B4"]);

    public string Provider
    {
        get => _provider;
        set
        {
            if (!SetField(ref _provider, value))
            {
                return;
            }

            RefreshModelsForProvider(preserveCurrentModel: false);
            ApplyProviderDefaults();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowBaseUrlSettings)));
        }
    }

    public string Model
    {
        get => _model;
        set => SetField(ref _model, value);
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set => SetField(ref _baseUrl, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetField(ref _apiKey, value);
    }

    public string EmbeddingProvider
    {
        get => _embeddingProvider;
        set
        {
            if (!SetField(ref _embeddingProvider, string.IsNullOrWhiteSpace(value) ? "none" : value))
            {
                return;
            }

            ApplyEmbeddingProviderDefaults();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowEmbeddingSettings)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowEmbeddingBaseUrlSettings)));
        }
    }

    public string EmbeddingModel
    {
        get => _embeddingModel;
        set => SetField(ref _embeddingModel, value);
    }

    public string EmbeddingBaseUrl
    {
        get => _embeddingBaseUrl;
        set => SetField(ref _embeddingBaseUrl, value);
    }

    public string EmbeddingApiKey
    {
        get => _embeddingApiKey;
        set => SetField(ref _embeddingApiKey, value);
    }

    public string WorkspaceRoot
    {
        get => _workspaceRoot;
        set => SetField(ref _workspaceRoot, value);
    }

    public string InputText
    {
        get => _inputText;
        set => SetField(ref _inputText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (!SetField(ref _statusText, value))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusAccentBrush)));
            RefreshRunSummary();
        }
    }

    public string StatusAccentBrush
    {
        get
        {
            if (StatusText.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                return "#F87171";
            }

            if (StatusText.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("needs", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                return "#FBBF24";
            }

            if (StatusText.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("saved", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("built", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("succeeded", StringComparison.OrdinalIgnoreCase) ||
                StatusText.Contains("passed", StringComparison.OrdinalIgnoreCase))
            {
                return "#37D67A";
            }

            return "#B7C4D1";
        }
    }

    public bool ShowBaseUrlSettings => Provider.Equals("custom", StringComparison.OrdinalIgnoreCase);

    public bool ShowEmbeddingSettings => !EmbeddingProvider.Equals("none", StringComparison.OrdinalIgnoreCase);

    public bool ShowEmbeddingBaseUrlSettings => EmbeddingProvider.Equals("custom", StringComparison.OrdinalIgnoreCase);

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => SetField(ref _timeoutSeconds, value);
    }

    public uint MaxTokens
    {
        get => _maxTokens;
        set => SetField(ref _maxTokens, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApproveAllAndVerify)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApprovePlan)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExecuteWorkerScaffold)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRunWorkerRepair)));
                RefreshRunSummary();
            }
        }
    }

    public AgentRunState CurrentRunState
    {
        get => _currentRunState;
        set
        {
            if (!SetField(ref _currentRunState, value))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentRunStateText)));
            RefreshRunSummary();
        }
    }

    public string CurrentRunStateText => CurrentRunState.ToString();

    public string UsageText
    {
        get => _usageText;
        set => SetField(ref _usageText, value);
    }

    public string RunPermissionStatusText
    {
        get => _runPermissionStatusText;
        set => SetField(ref _runPermissionStatusText, value);
    }

    public bool CanClearRunPermissions
    {
        get => _canClearRunPermissions;
        set => SetField(ref _canClearRunPermissions, value);
    }

    public double DesktopFontSize
    {
        get => _desktopFontSize;
        set => SetField(ref _desktopFontSize, Math.Clamp(value, 11, 22));
    }

    public bool AutoAttachWorkspaceContext
    {
        get => _autoAttachWorkspaceContext;
        set => SetField(ref _autoAttachWorkspaceContext, value);
    }

    public bool AutoFetchLinks
    {
        get => _autoFetchLinks;
        set => SetField(ref _autoFetchLinks, value);
    }

    public bool EnableScreenshotLlmVisionReview
    {
        get => _enableScreenshotLlmVisionReview;
        set => SetField(ref _enableScreenshotLlmVisionReview, value);
    }

    public string UiLanguage
    {
        get => _uiLanguage;
        set
        {
            if (!SetField(ref _uiLanguage, string.IsNullOrWhiteSpace(value) ? "English" : value))
            {
                return;
            }

            NotifyLocalizedTextChanged();
            Git.UseKoreanUi = IsKoreanUi;
            Project.UseKoreanUi = IsKoreanUi;
            EvalDashboard.UseKoreanUi = IsKoreanUi;
        }
    }

    public bool IsKoreanUi => DesktopLocalizer.IsKoreanUi(UiLanguage);

    public string MenuFileText => Ui(DesktopText.MenuFile);
    public string MenuSelectProjectFolderText => Ui(DesktopText.MenuSelectProjectFolder);
    public string MenuAddAttachmentText => Ui(DesktopText.MenuAddAttachment);
    public string MenuClearAttachmentsText => Ui(DesktopText.MenuClearAttachments);
    public string MenuExitText => Ui(DesktopText.MenuExit);
    public string MenuEditText => Ui(DesktopText.MenuEdit);
    public string MenuCopyLastAnswerText => Ui(DesktopText.MenuCopyLastAnswer);
    public string MenuCopyConversationText => Ui(DesktopText.MenuCopyConversation);
    public string MenuClearConversationText => Ui(DesktopText.MenuClearConversation);
    public string MenuSettingsText => Ui(DesktopText.MenuSettings);
    public string MenuSaveSettingsText => Ui(DesktopText.MenuSaveSettings);
    public string MenuViewText => Ui(DesktopText.MenuView);
    public string MenuIncreaseFontText => Ui(DesktopText.MenuIncreaseFont);
    public string MenuDecreaseFontText => Ui(DesktopText.MenuDecreaseFont);
    public string MenuResetFontText => Ui(DesktopText.MenuResetFont);
    public string MenuHelpText => Ui(DesktopText.MenuHelp);
    public string MenuShowStatusText => Ui(DesktopText.MenuShowStatus);
    public string SettingsHeaderText => Ui(DesktopText.SettingsHeader);
    public string SaveText => Ui(DesktopText.Save);
    public string UiLanguageText => Ui(DesktopText.UiLanguage);
    public string ProjectContextAutoAttachText => Ui(DesktopText.ProjectContextAutoAttach);
    public string AutoFetchLinksText => Ui(DesktopText.AutoFetchLinks);
    public string ProjectHeaderText => Ui(DesktopText.ProjectHeader);
    public string ProjectFolderText => Ui(DesktopText.ProjectFolder);
    public string BrowseFolderText => Ui(DesktopText.BrowseFolder);
    public string OpenFolderText => Ui(DesktopText.OpenFolder);
    public string OpenVSCodeText => Ui(DesktopText.OpenVSCode);
    public string BuildEmbeddingIndexText => Ui(DesktopText.BuildEmbeddingIndex);
    public string ChatHeaderText => Ui(DesktopText.ChatHeader);
    public string AttachFilesText => Ui(DesktopText.AttachFiles);
    public string CodeBlockText => Ui(DesktopText.CodeBlock);
    public string AddProjectFileText => Ui(DesktopText.AddProjectFile);
    public string ClearAttachmentsText => Ui(DesktopText.ClearAttachments);
    public string SendText => Ui(DesktopText.Send);
    public string CopyText => Ui(DesktopText.Copy);
    public string CopyWholeMessageText => Ui(DesktopText.CopyWholeMessage);
    public string ToolsHeaderText => Ui(DesktopText.ToolsHeader);
    public string ManageText => Ui(DesktopText.Manage);
    public string ReadFileToolText => Ui(DesktopText.ReadFileTool);
    public string WriteFileToolText => Ui(DesktopText.WriteFileTool);
    public string ShellExecuteToolText => Ui(DesktopText.ShellExecuteTool);
    public string SearchFilesToolText => Ui(DesktopText.SearchFilesTool);
    public string ListDirectoryToolText => Ui(DesktopText.ListDirectoryTool);
    public string StatusPanelText => Ui(DesktopText.StatusPanel);
    public string ClearText => Ui(DesktopText.Clear);
    public string RunLogText => Ui(DesktopText.RunLog);
    public string ChangePreviewText => Ui(DesktopText.ChangePreview);
    public string AllText => Ui(DesktopText.All);
    public string EvidenceTrailText => Ui(DesktopText.EvidenceTrail);
    public string EvalDashboardText => Ui(DesktopText.EvalDashboard);
    public string EvalDashboardRefreshText => Ui(DesktopText.EvalDashboardRefresh);
    public string EvalDashboardHelpText => Ui(DesktopText.EvalDashboardHelp);
    public string EvidenceTrailHelpText => Ui(DesktopText.EvidenceTrailHelp);
    public string SaveSummaryText => Ui(DesktopText.SaveSummary);
    public string LoadText => Ui(DesktopText.Load);
    public string ResumeText => Ui(DesktopText.Resume);
    public string LearningCandidatesText => Ui(DesktopText.LearningCandidates);
    public string LearningCandidatesHelpText => Ui(DesktopText.LearningCandidatesHelp);
    public string SaveLessonText => Ui(DesktopText.SaveLesson);
    public string DismissText => Ui(DesktopText.Dismiss);
    public string SavedMemoryText => Ui(DesktopText.SavedMemory);
    public string RefreshText => Ui(DesktopText.Refresh);
    public string DisableText => Ui(DesktopText.Disable);
    public string DeleteText => Ui(DesktopText.Delete);
    public string SessionSummaryText => Ui(DesktopText.SessionSummary);

    private string Ui(string key) => DesktopLocalizer.UiText(key, IsKoreanUi);

    public AgentWorkMode WorkMode
    {
        get => _workMode;
        set
        {
            if (!SetField(ref _workMode, value))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkModeDescription)));
        }
    }

    public string WorkModeDescription => WorkMode switch
    {
        AgentWorkMode.Readonly => "Read/search only. Blocks writes, shell, network, and Git changes.",
        AgentWorkMode.Coding => "Allows workspace edits and build/test commands with approval.",
        AgentWorkMode.FullAgent => "Allows broader shell and Git operations with approval.",
        _ => string.Empty
    };

    public bool CanFixLastVerificationFailure
    {
        get => Verification.CanFixLastFailure;
        set => Verification.CanFixLastFailure = value;
    }

    public string LastVerificationFailureSummary
    {
        get => Verification.LastFailureSummary;
        set => Verification.LastFailureSummary = value;
    }

    public bool CanFixLastCodeReviewFindings
    {
        get => Git.CanFixLastCodeReviewFindings;
        set => Git.CanFixLastCodeReviewFindings = value;
    }

    public bool CanContinueLastRun
    {
        get => _canContinueLastRun;
        set => SetField(ref _canContinueLastRun, value);
    }

    public string LastContinuationPrompt
    {
        get => _lastContinuationPrompt;
        set => SetField(ref _lastContinuationPrompt, value);
    }

    public bool CanResumeCheckpoint
    {
        get => _canResumeCheckpoint;
        set => SetField(ref _canResumeCheckpoint, value);
    }

    public string LatestCheckpointText
    {
        get => _latestCheckpointText;
        set => SetField(ref _latestCheckpointText, value);
    }

    public string GitStatusText
    {
        get => Git.StatusText;
        set => Git.StatusText = value;
    }

    public string GitDiffText
    {
        get => Git.DiffText;
        set => Git.DiffText = value;
    }

    public string GitSelectedFileDiffText
    {
        get => Git.SelectedFileDiffText;
        set => Git.SelectedFileDiffText = value;
    }

    public string GitLastUpdatedText
    {
        get => Git.LastUpdatedText;
        set => Git.LastUpdatedText = value;
    }

    public string GitCommitMessage
    {
        get => Git.CommitMessage;
        set => Git.CommitMessage = value;
    }

    public string WorkspaceAnalysisSummary
    {
        get => Project.AnalysisSummary;
        set => Project.AnalysisSummary = value;
    }

    public string WorkspaceProjectType
    {
        get => Project.ProjectType;
        set => Project.ProjectType = value;
    }

    public string WorkspaceFramework
    {
        get => Project.Framework;
        set => Project.Framework = value;
    }

    public string WorkspaceGitBranch
    {
        get => Project.GitBranch;
        set => Project.GitBranch = value;
    }

    public string WorkspaceStats
    {
        get => Project.Stats;
        set => Project.Stats = value;
    }

    public string WorkspaceAnalysisUpdatedText
    {
        get => Project.AnalysisUpdatedText;
        set => Project.AnalysisUpdatedText = value;
    }

    public string LatestSessionSummaryText
    {
        get => _latestSessionSummaryText;
        set => SetField(ref _latestSessionSummaryText, value);
    }

    public bool CanResumeSessionSummary
    {
        get => _canResumeSessionSummary;
        set => SetField(ref _canResumeSessionSummary, value);
    }

    public string ProjectConfigText
    {
        get => _projectConfigText;
        set => SetField(ref _projectConfigText, value);
    }

    public string SourceFileFilter
    {
        get => _sourceFileFilter;
        set => SetField(ref _sourceFileFilter, value);
    }

    public string SourceFilePreviewText
    {
        get => _sourceFilePreviewText;
        set => SetField(ref _sourceFilePreviewText, value);
    }

    public string ReviewWorkflowText
    {
        get => _reviewWorkflowText;
        set => SetField(ref _reviewWorkflowText, value);
    }

    public string PendingReviewVerificationText
    {
        get => _pendingReviewVerificationText;
        set => SetField(ref _pendingReviewVerificationText, value);
    }

    public string MemoryGcPreviewText
    {
        get => _memoryGcPreviewText;
        set => SetField(ref _memoryGcPreviewText, value);
    }

    public bool HasPendingReviewVerification
    {
        get => _hasPendingReviewVerification;
        set
        {
            if (!SetField(ref _hasPendingReviewVerification, value))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApproveAllAndVerify)));
        }
    }

    public bool CanApproveAllAndVerify =>
        HasPendingReviewVerification &&
        !IsBusy &&
        FileChanges.Count > 0 &&
        FileChanges.All(change => change.ReviewStatus is FileChangeReviewStatus.Pending or FileChangeReviewStatus.Approved);

    public string PlanEvidenceSummary
    {
        get => _planEvidenceSummary;
        set => SetField(ref _planEvidenceSummary, value);
    }

    public string PlanEvidenceStatusText
    {
        get => _planEvidenceStatusText;
        set => SetField(ref _planEvidenceStatusText, value);
    }

    public string PlanEvidenceAccentBrush
    {
        get => _planEvidenceAccentBrush;
        set => SetField(ref _planEvidenceAccentBrush, value);
    }

    public string PlanApprovalPreviewText
    {
        get => _planApprovalPreviewText;
        set => SetField(ref _planApprovalPreviewText, value);
    }

    public string PlanApprovalStateText
    {
        get => _planApprovalStateText;
        set => SetField(ref _planApprovalStateText, value);
    }

    public string PlanApprovalAccentBrush
    {
        get => _planApprovalAccentBrush;
        set => SetField(ref _planApprovalAccentBrush, value);
    }

    public bool HasPendingPlanApproval
    {
        get => _hasPendingPlanApproval;
        set
        {
            if (!SetField(ref _hasPendingPlanApproval, value))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApprovePlan)));
        }
    }

    public bool CanApprovePlan => HasPendingPlanApproval && !IsBusy;

    public bool CanExecuteWorkerScaffold => CurrentWorkerExecutionContext?.State == WorkerExecutionState.Ready && !IsBusy;

    public bool CanRunWorkerRepair => CurrentWorkerExecutionContext?.State == WorkerExecutionState.RepairRequired && !IsBusy;

    public WorkerExecutionContext? CurrentWorkerExecutionContext
    {
        get => _currentWorkerExecutionContext;
        set => SetField(ref _currentWorkerExecutionContext, value);
    }

    public string EvalDashboardSummary
    {
        get => EvalDashboard.Summary;
        set => EvalDashboard.Summary = value;
    }

    public string EvalDashboardUpdatedText
    {
        get => EvalDashboard.UpdatedText;
        set => EvalDashboard.UpdatedText = value;
    }

    public bool HasProjectConfig
    {
        get => _hasProjectConfig;
        set => SetField(ref _hasProjectConfig, value);
    }

    public FileChangeRecord? SelectedFileChange
    {
        get => _selectedFileChange;
        set => SetField(ref _selectedFileChange, value);
    }

    public SourceFileEntry? SelectedSourceFile
    {
        get => _selectedSourceFile;
        set => SetField(ref _selectedSourceFile, value);
    }

    public GitChangedFile? SelectedGitChangedFile
    {
        get => Git.SelectedChangedFile;
        set => Git.SelectedChangedFile = value;
    }

    public ObservableCollection<GitChangedFile> GitChangedFiles => Git.ChangedFiles;

    public ObservableCollection<AgentVerificationPlan> VerificationPlans => Verification.Plans;

    public ObservableCollection<VerificationResultCard> VerificationResults => Verification.Results;

    public AgentPlanItem? SelectedPlanItem
    {
        get => _selectedPlanItem;
        set
        {
            if (SetField(ref _selectedPlanItem, value))
            {
                RefreshPlanEvidenceSummary();
            }
        }
    }

    public ProjectMemoryLesson? SelectedPendingMemoryLesson
    {
        get => _selectedPendingMemoryLesson;
        set => SetField(ref _selectedPendingMemoryLesson, value);
    }

    public ProjectMemoryLesson? SelectedSavedMemoryLesson
    {
        get => _selectedSavedMemoryLesson;
        set => SetField(ref _selectedSavedMemoryLesson, value);
    }

    public ProviderConfiguration ToConfiguration()
    {
        return new ProviderConfiguration
        {
            Provider = Provider,
            Model = Model,
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            EmbeddingProvider = EmbeddingProvider,
            EmbeddingModel = EmbeddingModel,
            EmbeddingBaseUrl = EmbeddingBaseUrl,
            EmbeddingApiKey = EmbeddingApiKey,
            TimeoutSeconds = TimeoutSeconds,
            MaxTokens = MaxTokens,
            DesktopFontSize = DesktopFontSize,
            DesktopAutoAttachWorkspaceContext = AutoAttachWorkspaceContext,
            DesktopAutoFetchLinks = AutoFetchLinks,
            DesktopEnableScreenshotLlmVisionReview = EnableScreenshotLlmVisionReview,
            DesktopWorkMode = WorkMode.ToString(),
            DesktopUiLanguage = UiLanguage,
            DesktopMaxToolSteps = WorkMode switch
            {
                AgentWorkMode.Readonly => 20,
                AgentWorkMode.Coding => 50,
                AgentWorkMode.FullAgent => 50,
                _ => 50
            }
        };
    }

    public void ApplyConfiguration(ProviderConfiguration config)
    {
        _provider = string.IsNullOrWhiteSpace(config.Provider) ? "opencode-go" : config.Provider;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Provider)));
        RefreshModelsForProvider(preserveCurrentModel: true);
        Model = string.IsNullOrWhiteSpace(config.Model) ? DesktopProviderModelCatalog.GetDefaultModel(Provider) : config.Model;
        BaseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? ProviderConfiguration.OpenCodeGoDefaultBaseUrl : config.BaseUrl;
        ApiKey = config.ApiKey;
        EmbeddingProvider = string.IsNullOrWhiteSpace(config.EmbeddingProvider) ? "openai" : config.EmbeddingProvider;
        EmbeddingModel = string.IsNullOrWhiteSpace(config.EmbeddingModel) ? DesktopEmbeddingClientFactory.DefaultEmbeddingModel : config.EmbeddingModel;
        EmbeddingBaseUrl = string.IsNullOrWhiteSpace(config.EmbeddingBaseUrl) ? "https://api.openai.com/v1" : config.EmbeddingBaseUrl;
        EmbeddingApiKey = config.EmbeddingApiKey;
        TimeoutSeconds = config.TimeoutSeconds;
        MaxTokens = config.MaxTokens == 0 ? 4096 : config.MaxTokens;
        DesktopFontSize = config.DesktopFontSize <= 0 ? 14 : config.DesktopFontSize;
        AutoAttachWorkspaceContext = config.DesktopAutoAttachWorkspaceContext;
        AutoFetchLinks = config.DesktopAutoFetchLinks;
        EnableScreenshotLlmVisionReview = config.DesktopEnableScreenshotLlmVisionReview;
        UiLanguage = string.IsNullOrWhiteSpace(config.DesktopUiLanguage) ? "English" : config.DesktopUiLanguage;
        WorkMode = Enum.TryParse<AgentWorkMode>(config.DesktopWorkMode, ignoreCase: true, out var workMode)
            ? workMode
            : AgentWorkMode.Coding;
    }

    public void ApplyProviderModels(IReadOnlyList<string> models, bool preserveCurrentModel)
    {
        if (models.Count == 0)
        {
            return;
        }

        var currentModel = Model;
        AvailableModels.Clear();
        foreach (var model in models)
        {
            AvailableModels.Add(model);
        }

        if (preserveCurrentModel &&
            !string.IsNullOrWhiteSpace(currentModel) &&
            models.Contains(currentModel, StringComparer.OrdinalIgnoreCase))
        {
            Model = currentModel;
            return;
        }

        Model = models.Contains(currentModel, StringComparer.OrdinalIgnoreCase)
            ? currentModel
            : models[0];
    }

    public void AddLog(string message)
    {
        Logs.Add($"{DateTime.Now:HH:mm:ss}  INFO  {message}");
    }

    public void SetRunPermissionApprovals(IReadOnlyCollection<PermissionRiskLevel> approvedRiskLevels)
    {
        RunPermissionStatusText = DesktopPermissionEnforcer.FormatApprovedForRun(approvedRiskLevels);
        CanClearRunPermissions = approvedRiskLevels.Count > 0;
    }

    public void ClearRunPermissionStatus()
    {
        SetRunPermissionApprovals([]);
    }

    public void AddRunStep(AgentRunState state, string title, string? detail = null)
    {
        CurrentRunState = state;
        RunSteps.Add(new AgentRunStep
        {
            State = state,
            Title = title,
            Detail = detail ?? string.Empty,
            UseKoreanUi = IsKoreanUi
        });
        RefreshPlanEvidenceSummary();
    }

    public void SetLastVerificationFailure(string summary)
    {
        Verification.SetLastFailure(summary);
    }

    public void ClearLastVerificationFailure()
    {
        Verification.ClearLastFailure();
    }

    public void ClearSidePanelState()
    {
        Logs.Clear();
        RunSteps.Clear();
        Verification.Clear();
        FileChanges.Clear();
        EvalDashboard.Reset();
        RunSummary.Reset();
        ClearPendingReviewVerification();
        RefreshPlanEvidenceSummary();
        RefreshRunSummary();
    }

    public void AddVerificationResult(VerificationResultCard result)
    {
        Verification.AddResult(result);
        RefreshPlanEvidenceSummary();
        RefreshRunSummary();
    }

    public void SetPendingReviewVerification(AgentVerificationPlan plan, int changedFileCount, int nextAttempt, int maxAttempts)
    {
        HasPendingReviewVerification = true;
        PendingReviewVerificationText = string.IsNullOrWhiteSpace(plan.Command)
            ? "Queued verification: no command"
            : $"Queued verification: {plan.Command}";
        ReviewWorkflowText = $"Review {changedFileCount:0} changed file(s). Approve or mark edits, then run verification. Next attempt: {nextAttempt:0}/{maxAttempts:0}.";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApproveAllAndVerify)));
    }

    public void ClearPendingReviewVerification()
    {
        HasPendingReviewVerification = false;
        PendingReviewVerificationText = "No verification queued.";
        ReviewWorkflowText = "No auto verification is waiting. Review changes manually or start Auto fix from a failed verification.";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApproveAllAndVerify)));
    }

    public void SetPlanApprovalPreview(WorkerPlanPreview preview)
    {
        CurrentWorkerExecutionContext = null;
        PlanApprovalPreviewText = BuildPlanApprovalPreviewText(preview);
        PlanApprovalStateText = preview.ApprovalState switch
        {
            WorkerPlanApprovalState.Blocked => "Plan blocked",
            WorkerPlanApprovalState.NeedsApproval => "Plan approval required",
            _ => "Plan ready"
        };
        PlanApprovalAccentBrush = preview.ApprovalState switch
        {
            WorkerPlanApprovalState.Blocked => "#EF4444",
            WorkerPlanApprovalState.NeedsApproval => "#F59E0B",
            _ => "#37D67A"
        };
        HasPendingPlanApproval = preview.ApprovalState == WorkerPlanApprovalState.NeedsApproval;
        StatusText = preview.ApprovalState == WorkerPlanApprovalState.Blocked
            ? "Plan blocked by validation"
            : StatusText;
    }

    public void SetWorkerExecutionContext(WorkerExecutionContext context)
    {
        CurrentWorkerExecutionContext = context;
        PlanApprovalPreviewText = BuildPlanApprovalPreviewText(context.Preview);
        PlanApprovalStateText = context.State switch
        {
            WorkerExecutionState.Blocked => "Plan blocked",
            WorkerExecutionState.AwaitingApproval => "Plan approval required",
            WorkerExecutionState.Ready => "Plan ready",
            WorkerExecutionState.ScaffoldExecuted => "Scaffold executed",
            WorkerExecutionState.ScaffoldFailed => "Scaffold failed",
            WorkerExecutionState.Succeeded => "Plan verified",
            WorkerExecutionState.RepairRequired => "Plan repair required",
            WorkerExecutionState.StoppedRepeatedFailure => "Plan stopped",
            _ => "Plan ready"
        };
        PlanApprovalAccentBrush = context.State switch
        {
            WorkerExecutionState.Blocked or WorkerExecutionState.StoppedRepeatedFailure => "#EF4444",
            WorkerExecutionState.ScaffoldFailed => "#EF4444",
            WorkerExecutionState.AwaitingApproval or WorkerExecutionState.RepairRequired => "#F59E0B",
            _ => "#37D67A"
        };
        HasPendingPlanApproval = context.State == WorkerExecutionState.AwaitingApproval;
        StatusText = context.State == WorkerExecutionState.Blocked
            ? "Plan blocked by validation"
            : context.StatusMessage;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExecuteWorkerScaffold)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRunWorkerRepair)));
    }

    public void ApprovePlan()
    {
        if (!HasPendingPlanApproval)
        {
            return;
        }

        if (CurrentWorkerExecutionContext != null)
        {
            CurrentWorkerExecutionContext.State = WorkerExecutionState.Ready;
            CurrentWorkerExecutionContext.StatusMessage = "Worker plan approved.";
        }

        HasPendingPlanApproval = false;
        PlanApprovalStateText = "Plan approved";
        PlanApprovalAccentBrush = "#37D67A";
        StatusText = "Plan approved";
        AddLog("Plan approved");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExecuteWorkerScaffold)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRunWorkerRepair)));
    }

    public void ClearPlanApprovalPreview()
    {
        HasPendingPlanApproval = false;
        CurrentWorkerExecutionContext = null;
        PlanApprovalPreviewText = "No plan approval preview.";
        PlanApprovalStateText = "No approval needed";
        PlanApprovalAccentBrush = "#B7C4D1";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExecuteWorkerScaffold)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRunWorkerRepair)));
    }

    private void FileChangesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<FileChangeRecord>())
            {
                item.PropertyChanged += FileChangeOnPropertyChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<FileChangeRecord>())
            {
                item.PropertyChanged -= FileChangeOnPropertyChanged;
            }
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApproveAllAndVerify)));
        RefreshRunSummary();
    }

    private void FileChangeOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileChangeRecord.ReviewStatus) or nameof(FileChangeRecord.ReviewStatusText))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanApproveAllAndVerify)));
            RefreshRunSummary();
        }
    }

    private void RefreshRunSummary()
    {
        RunSummary.Update(
            CurrentRunState,
            StatusText,
            RunSteps,
            FileChanges,
            Verification.Results,
            IsBusy,
            IsKoreanUi);
    }

    public void ApplyWorkspaceAnalysis(WorkspaceAnalysis analysis)
    {
        Project.ApplyAnalysis(analysis);
    }

    public void ApplyEvalDashboard(EvalReplayDashboardReport report)
    {
        EvalDashboard.ApplyReport(report);
        RefreshPlanEvidenceSummary();
    }

    public void RefreshPlanEvidenceSummary()
    {
        var selected = SelectedPlanItem;
        var item = selected ??
                   PlanItems.FirstOrDefault(plan => plan.Status == AgentPlanItemStatus.InProgress) ??
                   PlanItems.FirstOrDefault(plan => plan.Status == AgentPlanItemStatus.Pending) ??
                   PlanItems.LastOrDefault(plan => plan.Status == AgentPlanItemStatus.Done);

        if (item == null)
        {
            PlanEvidenceStatusText = "No plan selected";
            PlanEvidenceAccentBrush = "#B7C4D1";
            PlanEvidenceSummary = "No plan evidence yet. Create or load a plan, then run an item to connect evidence and verification.";
            return;
        }

        var evidence = BuildCompactEvidence(RunSteps);
        var verification = BuildCompactVerification(Verification.Results.FirstOrDefault());
        var eval = EvalDashboard.Findings.FirstOrDefault() ?? EvalDashboard.FailureFingerprints.FirstOrDefault() ?? "No eval finding loaded.";
        PlanEvidenceStatusText = $"{item.StatusText}: {item.DisplayTitle}";
        PlanEvidenceAccentBrush = item.Status switch
        {
            AgentPlanItemStatus.Done => "#37D67A",
            AgentPlanItemStatus.InProgress => "#5BA7FF",
            AgentPlanItemStatus.Blocked => "#F87171",
            _ => "#FBBF24"
        };
        PlanEvidenceSummary = $"Evidence: {evidence} | Verification: {verification} | Eval: {TrimPlanEvidence(eval, 120)}";
    }

    private static string BuildPlanApprovalPreviewText(WorkerPlanPreview preview)
    {
        var summary = preview.ApprovalSummary;
        var files = summary.CreatedFiles
            .Concat(summary.ModifiedFiles)
            .Concat(summary.DeletedFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        var fileText = files.Count == 0 ? "Files: none detected" : $"Files: {string.Join(", ", files)}";
        var riskText = summary.RiskReasons.Count == 0
            ? $"Risk: {summary.RiskLevel}"
            : $"Risk: {summary.RiskLevel} ({string.Join("; ", summary.RiskReasons.Take(2))})";
        var changes = summary.ExpectedChanges.Count == 0
            ? "Expected changes: plan checklist only"
            : $"Expected changes: {string.Join("; ", summary.ExpectedChanges.Take(3))}";
        return $"{preview.DecisionSummary}{Environment.NewLine}{fileText}{Environment.NewLine}{changes}{Environment.NewLine}{riskText}";
    }

    private static string BuildCompactEvidence(IReadOnlyList<AgentRunStep> steps)
    {
        if (steps.Count == 0)
        {
            return "No timeline evidence yet.";
        }

        var visualStep = steps.LastOrDefault(step =>
            step.Title.Contains("visual attachment", StringComparison.OrdinalIgnoreCase));
        var latestStep = steps.LastOrDefault();
        if (visualStep == null || ReferenceEquals(visualStep, latestStep))
        {
            return TrimPlanEvidence(BuildStepDetail(latestStep), 120);
        }

        return TrimPlanEvidence($"{BuildStepDetail(visualStep)} | {BuildStepDetail(latestStep)}", 180);
    }

    private static string BuildStepDetail(AgentRunStep? step)
    {
        if (step == null)
        {
            return "No timeline evidence yet.";
        }

        return string.IsNullOrWhiteSpace(step.Detail) ? step.Title : $"{step.Title}: {step.Detail}";
    }

    private static string BuildCompactVerification(VerificationResultCard? result)
    {
        if (result == null)
        {
            return "Not verified.";
        }

        var detail = string.IsNullOrWhiteSpace(result.Summary) ? result.Title : $"{result.Status} {result.Title}: {result.Summary}";
        return TrimPlanEvidence(detail, 120);
    }

    private static string TrimPlanEvidence(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "No detail.";
        }

        value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= max ? value : value[..max] + "...";
    }

    private void RefreshModelsForProvider(bool preserveCurrentModel)
    {
        var currentModel = Model;
        AvailableModels.Clear();

        var models = DesktopProviderModelCatalog.GetModels(Provider);

        foreach (var model in models)
        {
            AvailableModels.Add(model);
        }

        if (preserveCurrentModel && !string.IsNullOrWhiteSpace(currentModel))
        {
            Model = currentModel;
            return;
        }

        Model = models[0];
    }

    private void ApplyProviderDefaults()
    {
        BaseUrl = DesktopProviderModelCatalog.GetDefaultBaseUrl(Provider, BaseUrl);
    }

    private void ApplyEmbeddingProviderDefaults()
    {
        if (EmbeddingProvider.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            EmbeddingBaseUrl = string.IsNullOrWhiteSpace(EmbeddingBaseUrl) ? "https://api.openai.com/v1" : EmbeddingBaseUrl;
            EmbeddingModel = string.IsNullOrWhiteSpace(EmbeddingModel) ? DesktopEmbeddingClientFactory.DefaultEmbeddingModel : EmbeddingModel;
            return;
        }

        if (EmbeddingProvider.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            EmbeddingModel = string.IsNullOrWhiteSpace(EmbeddingModel) ? DesktopEmbeddingClientFactory.DefaultEmbeddingModel : EmbeddingModel;
            return;
        }

        if (EmbeddingProvider.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            EmbeddingModel = string.Empty;
        }
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

    private void NotifyLocalizedTextChanged()
    {
        var names = new[]
        {
            nameof(IsKoreanUi),
            nameof(MenuFileText),
            nameof(MenuSelectProjectFolderText),
            nameof(MenuAddAttachmentText),
            nameof(MenuClearAttachmentsText),
            nameof(MenuExitText),
            nameof(MenuEditText),
            nameof(MenuCopyLastAnswerText),
            nameof(MenuCopyConversationText),
            nameof(MenuClearConversationText),
            nameof(MenuSettingsText),
            nameof(MenuSaveSettingsText),
            nameof(MenuViewText),
            nameof(MenuIncreaseFontText),
            nameof(MenuDecreaseFontText),
            nameof(MenuResetFontText),
            nameof(MenuHelpText),
            nameof(MenuShowStatusText),
            nameof(SettingsHeaderText),
            nameof(ShowBaseUrlSettings),
            nameof(ShowEmbeddingSettings),
            nameof(ShowEmbeddingBaseUrlSettings),
            nameof(SaveText),
            nameof(UiLanguageText),
            nameof(ProjectContextAutoAttachText),
            nameof(AutoFetchLinksText),
            nameof(ProjectHeaderText),
            nameof(ProjectFolderText),
            nameof(BrowseFolderText),
            nameof(OpenFolderText),
            nameof(OpenVSCodeText),
            nameof(BuildEmbeddingIndexText),
            nameof(ChatHeaderText),
            nameof(AttachFilesText),
            nameof(CodeBlockText),
            nameof(AddProjectFileText),
            nameof(ClearAttachmentsText),
            nameof(SendText),
            nameof(CopyText),
            nameof(CopyWholeMessageText),
            nameof(ToolsHeaderText),
            nameof(ManageText),
            nameof(ReadFileToolText),
            nameof(WriteFileToolText),
            nameof(ShellExecuteToolText),
            nameof(SearchFilesToolText),
            nameof(ListDirectoryToolText),
            nameof(StatusPanelText),
            nameof(ClearText),
            nameof(RunLogText),
            nameof(ChangePreviewText),
            nameof(AllText),
            nameof(EvidenceTrailText),
            nameof(EvalDashboardText),
            nameof(EvalDashboardRefreshText),
            nameof(EvalDashboardHelpText),
            nameof(EvidenceTrailHelpText),
            nameof(SaveSummaryText),
            nameof(LoadText),
            nameof(ResumeText),
            nameof(LearningCandidatesText),
            nameof(LearningCandidatesHelpText),
            nameof(SaveLessonText),
            nameof(DismissText),
            nameof(SavedMemoryText),
            nameof(RefreshText),
            nameof(DisableText),
            nameof(DeleteText),
            nameof(SessionSummaryText)
        };

        foreach (var name in names)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private static string? GetLegacyGitPropertyName(string? propertyName)
    {
        return propertyName switch
        {
            nameof(GitPanelViewModel.CanFixLastCodeReviewFindings) => nameof(CanFixLastCodeReviewFindings),
            nameof(GitPanelViewModel.StatusText) => nameof(GitStatusText),
            nameof(GitPanelViewModel.DiffText) => nameof(GitDiffText),
            nameof(GitPanelViewModel.SelectedFileDiffText) => nameof(GitSelectedFileDiffText),
            nameof(GitPanelViewModel.LastUpdatedText) => nameof(GitLastUpdatedText),
            nameof(GitPanelViewModel.CommitMessage) => nameof(GitCommitMessage),
            nameof(GitPanelViewModel.SelectedChangedFile) => nameof(SelectedGitChangedFile),
            _ => null
        };
    }

    private static string? GetLegacyVerificationPropertyName(string? propertyName)
    {
        return propertyName switch
        {
            nameof(VerificationPanelViewModel.CanFixLastFailure) => nameof(CanFixLastVerificationFailure),
            nameof(VerificationPanelViewModel.LastFailureSummary) => nameof(LastVerificationFailureSummary),
            _ => null
        };
    }

    private static string? GetLegacyProjectPropertyName(string? propertyName)
    {
        return propertyName switch
        {
            nameof(ProjectPanelViewModel.AnalysisSummary) => nameof(WorkspaceAnalysisSummary),
            nameof(ProjectPanelViewModel.ProjectType) => nameof(WorkspaceProjectType),
            nameof(ProjectPanelViewModel.Framework) => nameof(WorkspaceFramework),
            nameof(ProjectPanelViewModel.GitBranch) => nameof(WorkspaceGitBranch),
            nameof(ProjectPanelViewModel.Stats) => nameof(WorkspaceStats),
            nameof(ProjectPanelViewModel.AnalysisUpdatedText) => nameof(WorkspaceAnalysisUpdatedText),
            _ => null
        };
    }

    private static string? GetLegacyEvalDashboardPropertyName(string? propertyName)
    {
        return propertyName switch
        {
            nameof(EvalDashboardViewModel.Summary) => nameof(EvalDashboardSummary),
            nameof(EvalDashboardViewModel.UpdatedText) => nameof(EvalDashboardUpdatedText),
            _ => null
        };
    }
}
