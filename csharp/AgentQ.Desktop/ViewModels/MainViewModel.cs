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
    private string _uiLanguage = "English";
    private AgentWorkMode _workMode = AgentWorkMode.Coding;
    private bool _isBusy;
    private bool _canResumeCheckpoint;
    private bool _canContinueLastRun;
    private string _lastContinuationPrompt = string.Empty;
    private string _latestCheckpointText = "No checkpoint loaded.";
    private string _latestSessionSummaryText = "No session summary saved.";
    private string _projectConfigText = "No project config loaded.";
    private string _reviewWorkflowText = "No auto verification is waiting. Review changes manually or start Auto fix from a failed verification.";
    private string _pendingReviewVerificationText = "No verification queued.";
    private string _planEvidenceSummary = "No plan evidence yet. Create or load a plan, then run an item to connect evidence and verification.";
    private string _planEvidenceStatusText = "No plan selected";
    private string _planEvidenceAccentBrush = "#B7C4D1";
    private string _usageText = "\uC0AC\uC6A9\uB7C9 \uC815\uBCF4 \uC5C6\uC74C";
    private string _runPermissionStatusText = "Run permissions: none";
    private bool _canClearRunPermissions;
    private bool _hasProjectConfig;
    private bool _canResumeSessionSummary;
    private bool _hasPendingReviewVerification;
    private FileChangeRecord? _selectedFileChange;
    private AgentPlanItem? _selectedPlanItem;
    private ProjectMemoryLesson? _selectedPendingMemoryLesson;
    private ProjectMemoryLesson? _selectedSavedMemoryLesson;
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
        }
    }

    public bool IsKoreanUi => UiLanguage.Equals("\uD55C\uAD6D\uC5B4", StringComparison.OrdinalIgnoreCase) ||
                              UiLanguage.Equals("Korean", StringComparison.OrdinalIgnoreCase);

    public string MenuFileText => IsKoreanUi ? "\uD30C\uC77C" : "File";
    public string MenuSelectProjectFolderText => IsKoreanUi ? "\uD504\uB85C\uC81D\uD2B8 \uD3F4\uB354 \uC120\uD0DD" : "Select project folder";
    public string MenuAddAttachmentText => IsKoreanUi ? "\uCCA8\uBD80 \uCD94\uAC00" : "Add attachment";
    public string MenuClearAttachmentsText => IsKoreanUi ? "\uCCA8\uBD80 \uC9C0\uC6B0\uAE30" : "Clear attachments";
    public string MenuExitText => IsKoreanUi ? "\uC885\uB8CC" : "Exit";
    public string MenuEditText => IsKoreanUi ? "\uD3B8\uC9D1" : "Edit";
    public string MenuCopyLastAnswerText => IsKoreanUi ? "\uB9C8\uC9C0\uB9C9 \uB2F5\uBCC0 \uBCF5\uC0AC" : "Copy last answer";
    public string MenuCopyConversationText => IsKoreanUi ? "\uC804\uCCB4 \uB300\uD654 \uBCF5\uC0AC" : "Copy conversation";
    public string MenuClearConversationText => IsKoreanUi ? "\uB300\uD654 \uCD08\uAE30\uD654" : "Clear conversation";
    public string MenuSettingsText => IsKoreanUi ? "\uC124\uC815" : "Settings";
    public string MenuSaveSettingsText => IsKoreanUi ? "\uC124\uC815 \uC800\uC7A5" : "Save settings";
    public string MenuViewText => IsKoreanUi ? "\uBCF4\uAE30" : "View";
    public string MenuIncreaseFontText => IsKoreanUi ? "\uAE00\uC790 \uD06C\uAC8C" : "Increase font";
    public string MenuDecreaseFontText => IsKoreanUi ? "\uAE00\uC790 \uC791\uAC8C" : "Decrease font";
    public string MenuResetFontText => IsKoreanUi ? "\uAE30\uBCF8 \uAE00\uC790 \uD06C\uAE30" : "Reset font size";
    public string MenuHelpText => IsKoreanUi ? "\uB3C4\uC6C0\uB9D0" : "Help";
    public string MenuShowStatusText => IsKoreanUi ? "\uC0C1\uD0DC \uBCF4\uAE30" : "Show status";
    public string SettingsHeaderText => IsKoreanUi ? "\uC124\uC815" : "Settings";
    public string SaveText => IsKoreanUi ? "\uC800\uC7A5" : "Save";
    public string UiLanguageText => IsKoreanUi ? "UI \uC5B8\uC5B4" : "UI Language";
    public string ProjectContextAutoAttachText => IsKoreanUi ? "\uD504\uB85C\uC81D\uD2B8 \uCEE8\uD14D\uC2A4\uD2B8 \uC790\uB3D9 \uCCA8\uBD80" : "Auto attach project context";
    public string AutoFetchLinksText => IsKoreanUi ? "\uB9C1\uD06C \uC790\uB3D9 \uC77D\uAE30" : "Auto fetch links";
    public string ProjectHeaderText => IsKoreanUi ? "\uD504\uB85C\uC81D\uD2B8" : "Project";
    public string ProjectFolderText => IsKoreanUi ? "\uD504\uB85C\uC81D\uD2B8 \uD3F4\uB354" : "Project folder";
    public string BrowseFolderText => IsKoreanUi ? "\uD3F4\uB354 \uC120\uD0DD" : "Browse";
    public string OpenFolderText => IsKoreanUi ? "\uD3F4\uB354 \uC5F4\uAE30" : "Open";
    public string BuildEmbeddingIndexText => IsKoreanUi ? "\uC784\uBCA0\uB529 \uC778\uB371\uC2A4 \uC0DD\uC131" : "Build embedding index";
    public string ChatHeaderText => IsKoreanUi ? "\uC0C8 \uB300\uD654" : "New chat";
    public string AttachFilesText => IsKoreanUi ? "\uCCA8\uBD80" : "Attach";
    public string CodeBlockText => IsKoreanUi ? "\uCF54\uB4DC \uBE14\uB85D" : "Code block";
    public string AddProjectFileText => IsKoreanUi ? "\uD504\uB85C\uC81D\uD2B8 \uD30C\uC77C \uCD94\uAC00" : "Add project file";
    public string ClearAttachmentsText => IsKoreanUi ? "\uCCA8\uBD80 \uC9C0\uC6B0\uAE30" : "Clear";
    public string SendText => IsKoreanUi ? "\uC804\uC1A1\nCtrl+Enter" : "Send\nCtrl+Enter";
    public string CopyText => IsKoreanUi ? "\uBCF5\uC0AC" : "Copy";
    public string CopyWholeMessageText => IsKoreanUi ? "\uBA54\uC2DC\uC9C0 \uC804\uCCB4 \uBCF5\uC0AC" : "Copy whole message";
    public string ToolsHeaderText => IsKoreanUi ? "\uB3C4\uAD6C" : "Tools";
    public string ManageText => IsKoreanUi ? "\uAD00\uB9AC" : "Manage";
    public string ReadFileToolText => IsKoreanUi ? "read_file - \uD30C\uC77C \uB0B4\uC6A9\uC744 \uC77D\uC2B5\uB2C8\uB2E4" : "read_file - Read file contents";
    public string WriteFileToolText => IsKoreanUi ? "write_file - \uD30C\uC77C\uC744 \uC218\uC815\uD569\uB2C8\uB2E4" : "write_file - Edit files";
    public string ShellExecuteToolText => IsKoreanUi ? "shell_execute - \uBA85\uB839\uC744 \uC2E4\uD589\uD569\uB2C8\uB2E4" : "shell_execute - Run commands";
    public string SearchFilesToolText => IsKoreanUi ? "search_files - \uD30C\uC77C\uC744 \uAC80\uC0C9\uD569\uB2C8\uB2E4" : "search_files - Search files";
    public string ListDirectoryToolText => IsKoreanUi ? "list_directory - \uBAA9\uB85D\uC744 \uBD05\uB2C8\uB2E4" : "list_directory - List directories";
    public string StatusPanelText => IsKoreanUi ? "\uC0C1\uD0DC \uD328\uB110" : "Status panel";
    public string ClearText => IsKoreanUi ? "\uBE44\uC6B0\uAE30" : "Clear";
    public string RunLogText => IsKoreanUi ? "\uC791\uC5C5 \uB85C\uADF8" : "Run log";
    public string ChangePreviewText => IsKoreanUi ? "\uBCC0\uACBD \uBBF8\uB9AC\uBCF4\uAE30" : "Change preview";
    public string AllText => IsKoreanUi ? "\uC804\uCCB4" : "ALL";
    public string EvidenceTrailText => IsKoreanUi ? "\uADFC\uAC70 \uD750\uB984" : "Evidence";
    public string EvalDashboardText => IsKoreanUi ? "\uD3C9\uAC00" : "Eval";
    public string EvalDashboardRefreshText => IsKoreanUi ? "\uC0C8\uB85C\uACE0\uCE68" : "Refresh";
    public string EvalDashboardHelpText => IsKoreanUi
        ? "\uCD5C\uC2E0 replay, telemetry, \uAC80\uC99D \uACB0\uACFC, \uBC18\uBCF5 \uC2E4\uD328 fingerprint\uB97C \uC694\uC57D\uD569\uB2C8\uB2E4."
        : "Summarizes latest replay, telemetry, verification results, and recurring failure fingerprints.";
    public string EvidenceTrailHelpText => IsKoreanUi
        ? "\uC228\uC740 \uC0AC\uACE0 \uACFC\uC815\uC774 \uC544\uB2CC, \uC0AC\uC6A9\uD55C \uBA54\uBAA8\uB9AC, \uD30C\uC77C, \uAC80\uC0C9, \uBA85\uB839, \uAC80\uC99D \uD750\uB984\uC744 \uBCF4\uC5EC\uC90D\uB2C8\uB2E4."
        : "Shows used memory, files, searches, commands, changes, and verification flow instead of hidden model reasoning.";
    public string SaveSummaryText => IsKoreanUi ? "\uC694\uC57D \uC800\uC7A5" : "Save summary";
    public string LoadText => IsKoreanUi ? "\uBD88\uB7EC\uC624\uAE30" : "Load";
    public string ResumeText => IsKoreanUi ? "\uC774\uC5B4\uC11C" : "Resume";
    public string LearningCandidatesText => IsKoreanUi ? "\uD559\uC2B5 \uD6C4\uBCF4" : "Learning candidates";
    public string LearningCandidatesHelpText => IsKoreanUi
        ? "\uC791\uC5C5 \uD6C4 AgentQ\uAC00 \uB2E4\uC74C\uC5D0 \uAE30\uC5B5\uD558\uBA74 \uC88B\uC744 \uADDC\uCE59\uC744 \uC81C\uC548\uD569\uB2C8\uB2E4. \uC2B9\uC778\uD55C \uD56D\uBAA9\uB9CC \uC774 \uD504\uB85C\uC81D\uD2B8\uC758 \uB85C\uCEEC \uBA54\uBAA8\uB9AC\uC5D0 \uC800\uC7A5\uB429\uB2C8\uB2E4."
        : "After a run, AgentQ may suggest rules worth remembering. Only approved items are saved to this project's local memory.";
    public string SaveLessonText => IsKoreanUi ? "\uD559\uC2B5 \uC800\uC7A5" : "Save lesson";
    public string DismissText => IsKoreanUi ? "\uBB34\uC2DC" : "Dismiss";
    public string SavedMemoryText => IsKoreanUi ? "\uC800\uC7A5\uB41C \uBA54\uBAA8\uB9AC" : "Saved memory";
    public string RefreshText => IsKoreanUi ? "\uC0C8\uB85C\uACE0\uCE68" : "Refresh";
    public string DisableText => IsKoreanUi ? "\uBE44\uD65C\uC131" : "Disable";
    public string DeleteText => IsKoreanUi ? "\uC0AD\uC81C" : "Delete";
    public string SessionSummaryText => IsKoreanUi ? "\uC138\uC158 \uC694\uC57D" : "Session summary";
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
            Detail = detail ?? string.Empty
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
            IsBusy);
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
