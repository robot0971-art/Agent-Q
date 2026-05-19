using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Core.Providers;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly Dictionary<string, string[]> ModelCatalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["opencode-go"] =
        [
            "kimi-k2.6",
            "kimi-k2.5",
            "deepseek-v4-pro",
            "deepseek-v4-flash",
            "glm-5.1",
            "glm-5",
            "mimo-v2.5-pro",
            "mimo-v2.5"
        ],
        ["openai"] =
        [
            "gpt-5.2",
            "gpt-5.1",
            "gpt-4.1",
            "gpt-4.1-mini",
            "gpt-4o",
            "gpt-4o-mini",
            "o3",
            "o4-mini"
        ],
        ["anthropic"] =
        [
            "claude-opus-4-1",
            "claude-opus-4",
            "claude-sonnet-4-5",
            "claude-sonnet-4",
            "claude-3-7-sonnet-latest",
            "claude-3-5-haiku-latest"
        ],
        ["google"] =
        [
            "gemini-2.5-pro",
            "gemini-2.5-flash",
            "gemini-2.0-flash"
        ],
        ["xai"] =
        [
            "grok-4",
            "grok-3",
            "grok-3-mini"
        ],
        ["deepseek"] =
        [
            "deepseek-chat",
            "deepseek-reasoner"
        ]
    };

    private string _provider = "opencode-go";
    private string _model = "kimi-k2.6";
    private string _baseUrl = ProviderConfiguration.OpenCodeGoDefaultBaseUrl;
    private string _apiKey = string.Empty;
    private string _workspaceRoot = Environment.CurrentDirectory;
    private string _inputText = string.Empty;
    private string _statusText = "Ready";
    private int _timeoutSeconds;
    private uint _maxTokens = 4096;
    private double _desktopFontSize = 14;
    private bool _autoAttachWorkspaceContext = true;
    private bool _autoFetchLinks = true;
    private AgentWorkMode _workMode = AgentWorkMode.Coding;
    private bool _isBusy;
    private bool _canFixLastVerificationFailure;
    private bool _canFixLastCodeReviewFindings;
    private bool _canResumeCheckpoint;
    private bool _canContinueLastRun;
    private string _lastVerificationFailureSummary = string.Empty;
    private string _lastContinuationPrompt = string.Empty;
    private string _latestCheckpointText = "No checkpoint loaded.";
    private string _gitStatusText = "Not refreshed yet.";
    private string _gitDiffText = "Not refreshed yet.";
    private string _gitSelectedFileDiffText = "Select a changed file to view its diff.";
    private string _gitLastUpdatedText = "Git not refreshed yet.";
    private string _workspaceAnalysisSummary = "Workspace not analyzed yet.";
    private string _workspaceProjectType = "Unknown";
    private string _workspaceFramework = "Unknown";
    private string _workspaceGitBranch = "Unknown";
    private string _workspaceStats = "No stats yet.";
    private string _workspaceAnalysisUpdatedText = "Not analyzed yet.";
    private string _latestSessionSummaryText = "No session summary saved.";
    private string _projectConfigText = "No project config loaded.";
    private string _usageText = "사용량 정보 없음";
    private bool _hasProjectConfig;
    private bool _canResumeSessionSummary;
    private GitChangedFile? _selectedGitChangedFile;
    private AgentPlanItem? _selectedPlanItem;
    private AgentRunState _currentRunState = AgentRunState.Idle;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<string> Logs { get; } = [];

    public ObservableCollection<FileChangeRecord> FileChanges { get; } = [];

    public ObservableCollection<AgentRunStep> RunSteps { get; } = [];

    public ObservableCollection<AgentVerificationPlan> VerificationPlans { get; } = [];

    public ObservableCollection<VerificationResultCard> VerificationResults { get; } = [];

    public ObservableCollection<GitChangedFile> GitChangedFiles { get; } = [];

    public ObservableCollection<AgentPlanItem> PlanItems { get; } = [];

    public ObservableCollection<string> WorkspaceVerificationCommands { get; } = [];

    public ObservableCollection<string> WorkspaceHints { get; } = [];

    public ObservableCollection<string> Attachments { get; } = [];

    public ObservableCollection<string> AvailableProviders { get; } = new(ModelCatalog.Keys);

    public ObservableCollection<string> AvailableModels { get; } = new(ModelCatalog["opencode-go"]);

    public ObservableCollection<AgentWorkMode> AvailableWorkModes { get; } = new(Enum.GetValues<AgentWorkMode>());

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
        set => SetField(ref _statusText, value);
    }

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
        set => SetField(ref _isBusy, value);
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
        }
    }

    public string CurrentRunStateText => CurrentRunState.ToString();

    public string UsageText
    {
        get => _usageText;
        set => SetField(ref _usageText, value);
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
        get => _canFixLastVerificationFailure;
        set => SetField(ref _canFixLastVerificationFailure, value);
    }

    public string LastVerificationFailureSummary
    {
        get => _lastVerificationFailureSummary;
        set => SetField(ref _lastVerificationFailureSummary, value);
    }

    public bool CanFixLastCodeReviewFindings
    {
        get => _canFixLastCodeReviewFindings;
        set => SetField(ref _canFixLastCodeReviewFindings, value);
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
        get => _gitStatusText;
        set => SetField(ref _gitStatusText, value);
    }

    public string GitDiffText
    {
        get => _gitDiffText;
        set => SetField(ref _gitDiffText, value);
    }

    public string GitSelectedFileDiffText
    {
        get => _gitSelectedFileDiffText;
        set => SetField(ref _gitSelectedFileDiffText, value);
    }

    public string GitLastUpdatedText
    {
        get => _gitLastUpdatedText;
        set => SetField(ref _gitLastUpdatedText, value);
    }

    public string WorkspaceAnalysisSummary
    {
        get => _workspaceAnalysisSummary;
        set => SetField(ref _workspaceAnalysisSummary, value);
    }

    public string WorkspaceProjectType
    {
        get => _workspaceProjectType;
        set => SetField(ref _workspaceProjectType, value);
    }

    public string WorkspaceFramework
    {
        get => _workspaceFramework;
        set => SetField(ref _workspaceFramework, value);
    }

    public string WorkspaceGitBranch
    {
        get => _workspaceGitBranch;
        set => SetField(ref _workspaceGitBranch, value);
    }

    public string WorkspaceStats
    {
        get => _workspaceStats;
        set => SetField(ref _workspaceStats, value);
    }

    public string WorkspaceAnalysisUpdatedText
    {
        get => _workspaceAnalysisUpdatedText;
        set => SetField(ref _workspaceAnalysisUpdatedText, value);
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

    public bool HasProjectConfig
    {
        get => _hasProjectConfig;
        set => SetField(ref _hasProjectConfig, value);
    }

    public GitChangedFile? SelectedGitChangedFile
    {
        get => _selectedGitChangedFile;
        set => SetField(ref _selectedGitChangedFile, value);
    }

    public AgentPlanItem? SelectedPlanItem
    {
        get => _selectedPlanItem;
        set => SetField(ref _selectedPlanItem, value);
    }

    public ProviderConfiguration ToConfiguration()
    {
        return new ProviderConfiguration
        {
            Provider = Provider,
            Model = Model,
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            TimeoutSeconds = TimeoutSeconds,
            MaxTokens = MaxTokens,
            DesktopFontSize = DesktopFontSize,
            DesktopAutoAttachWorkspaceContext = AutoAttachWorkspaceContext,
            DesktopAutoFetchLinks = AutoFetchLinks,
            DesktopWorkMode = WorkMode.ToString(),
            DesktopMaxToolSteps = WorkMode switch
            {
                AgentWorkMode.Readonly => 8,
                AgentWorkMode.Coding => 12,
                AgentWorkMode.FullAgent => 16,
                _ => 12
            }
        };
    }

    public void ApplyConfiguration(ProviderConfiguration config)
    {
        _provider = string.IsNullOrWhiteSpace(config.Provider) ? "opencode-go" : config.Provider;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Provider)));
        RefreshModelsForProvider(preserveCurrentModel: true);
        Model = string.IsNullOrWhiteSpace(config.Model) ? GetDefaultModel(Provider) : config.Model;
        BaseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? ProviderConfiguration.OpenCodeGoDefaultBaseUrl : config.BaseUrl;
        ApiKey = config.ApiKey;
        TimeoutSeconds = config.TimeoutSeconds;
        MaxTokens = config.MaxTokens == 0 ? 4096 : config.MaxTokens;
        DesktopFontSize = config.DesktopFontSize <= 0 ? 14 : config.DesktopFontSize;
        AutoAttachWorkspaceContext = config.DesktopAutoAttachWorkspaceContext;
        AutoFetchLinks = config.DesktopAutoFetchLinks;
        WorkMode = Enum.TryParse<AgentWorkMode>(config.DesktopWorkMode, ignoreCase: true, out var workMode)
            ? workMode
            : AgentWorkMode.Coding;
    }

    public void AddLog(string message)
    {
        Logs.Add($"{DateTime.Now:HH:mm:ss}  INFO  {message}");
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
    }

    public void SetLastVerificationFailure(string summary)
    {
        LastVerificationFailureSummary = summary;
        CanFixLastVerificationFailure = true;
    }

    public void ClearLastVerificationFailure()
    {
        LastVerificationFailureSummary = string.Empty;
        CanFixLastVerificationFailure = false;
    }

    public void AddVerificationResult(VerificationResultCard result)
    {
        VerificationResults.Insert(0, result);
        while (VerificationResults.Count > 8)
        {
            VerificationResults.RemoveAt(VerificationResults.Count - 1);
        }
    }

    public void ApplyWorkspaceAnalysis(WorkspaceAnalysis analysis)
    {
        WorkspaceAnalysisSummary = analysis.Summary;
        WorkspaceProjectType = analysis.ProjectType;
        WorkspaceFramework = analysis.Framework;
        WorkspaceGitBranch = analysis.GitBranch;
        WorkspaceStats = $"{analysis.FileCount:0} files / {analysis.DirectoryCount:0} folders";
        WorkspaceAnalysisUpdatedText = $"Updated: {analysis.UpdatedAt:HH:mm:ss}";

        WorkspaceVerificationCommands.Clear();
        foreach (var command in analysis.VerificationCommands)
        {
            WorkspaceVerificationCommands.Add(command);
        }

        WorkspaceHints.Clear();
        foreach (var hint in analysis.Hints)
        {
            WorkspaceHints.Add(hint);
        }
    }

    private void RefreshModelsForProvider(bool preserveCurrentModel)
    {
        var currentModel = Model;
        AvailableModels.Clear();

        var models = ModelCatalog.TryGetValue(Provider, out var catalogModels)
            ? catalogModels
            : ["default"];

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
        BaseUrl = Provider.ToLowerInvariant() switch
        {
            "opencode-go" => ProviderConfiguration.OpenCodeGoDefaultBaseUrl,
            "openai" => "https://api.openai.com/v1",
            "anthropic" => "https://api.anthropic.com",
            "google" => "https://generativelanguage.googleapis.com/v1beta/openai",
            "xai" => "https://api.x.ai/v1",
            "deepseek" => "https://api.deepseek.com",
            _ => BaseUrl
        };
    }

    private static string GetDefaultModel(string provider)
    {
        return ModelCatalog.TryGetValue(provider, out var models) ? models[0] : "default";
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
