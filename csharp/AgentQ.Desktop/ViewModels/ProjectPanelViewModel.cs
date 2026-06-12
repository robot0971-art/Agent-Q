using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class ProjectPanelViewModel : INotifyPropertyChanged
{
    private bool _useKoreanUi;
    private string _analysisSummary = DesktopLocalizer.UiText(DesktopText.ProjectNotAnalyzed, useKoreanUi: false);
    private string _projectType = DesktopLocalizer.UiText(DesktopText.ProjectAnalyzeToDetect, useKoreanUi: false);
    private string _framework = DesktopLocalizer.UiText(DesktopText.ProjectAnalyzeToDetect, useKoreanUi: false);
    private string _gitBranch = DesktopLocalizer.UiText(DesktopText.ProjectAnalyzeToDetect, useKoreanUi: false);
    private string _stats = DesktopLocalizer.UiText(DesktopText.ProjectAnalyzeStats, useKoreanUi: false);
    private string _analysisUpdatedText = DesktopLocalizer.UiText(DesktopText.ProjectAnalysisUpdatedEmpty, useKoreanUi: false);
    private string _dashboardSummary = DesktopLocalizer.UiText(DesktopText.ProjectDashboardEmpty, useKoreanUi: false);
    private string _healthText = DesktopLocalizer.UiText(DesktopText.ProjectWaitingForAnalysis, useKoreanUi: false);
    private string _healthAccentBrush = "#B7C4D1";
    private string _symbolCountText = "0 symbols";
    private string _dependencyCountText = "0 dependencies";
    private string _keyFileCountText = "0 key files";
    private string _verificationCommandCountText = "0 commands";
    private string _projectMapEmptyText = DesktopLocalizer.UiText(DesktopText.ProjectMapEmpty, useKoreanUi: false);
    private string _keySymbolsEmptyText = DesktopLocalizer.UiText(DesktopText.ProjectSymbolsEmpty, useKoreanUi: false);
    private string _keyDependenciesEmptyText = DesktopLocalizer.UiText(DesktopText.ProjectDependenciesEmpty, useKoreanUi: false);
    private string _keyFilesEmptyText = DesktopLocalizer.UiText(DesktopText.ProjectFilesEmpty, useKoreanUi: false);
    private string _verificationCommandsEmptyText = DesktopLocalizer.UiText(DesktopText.ProjectVerificationEmpty, useKoreanUi: false);

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> VerificationCommands { get; } = [];

    public ObservableCollection<string> ProjectMap { get; } = [];

    public ObservableCollection<string> KeySymbols { get; } = [];

    public ObservableCollection<string> KeyDependencies { get; } = [];

    public ObservableCollection<string> KeyFiles { get; } = [];

    public ObservableCollection<string> Hints { get; } = [];

    public ObservableCollection<WorkerScaffoldRecommendation> ScaffoldRecommendations { get; } = [];

    public bool UseKoreanUi
    {
        get => _useKoreanUi;
        set
        {
            var previous = _useKoreanUi;
            if (!SetField(ref _useKoreanUi, value))
            {
                return;
            }

            RefreshDefaultText(previous);
        }
    }

    public string AnalysisSummary
    {
        get => _analysisSummary;
        set => SetField(ref _analysisSummary, value);
    }

    public string ProjectType
    {
        get => _projectType;
        set => SetField(ref _projectType, value);
    }

    public string Framework
    {
        get => _framework;
        set => SetField(ref _framework, value);
    }

    public string GitBranch
    {
        get => _gitBranch;
        set => SetField(ref _gitBranch, value);
    }

    public string Stats
    {
        get => _stats;
        set => SetField(ref _stats, value);
    }

    public string AnalysisUpdatedText
    {
        get => _analysisUpdatedText;
        set => SetField(ref _analysisUpdatedText, value);
    }

    public string DashboardSummary
    {
        get => _dashboardSummary;
        set => SetField(ref _dashboardSummary, value);
    }

    public string HealthText
    {
        get => _healthText;
        set => SetField(ref _healthText, value);
    }

    public string HealthAccentBrush
    {
        get => _healthAccentBrush;
        set => SetField(ref _healthAccentBrush, value);
    }

    public string SymbolCountText
    {
        get => _symbolCountText;
        set => SetField(ref _symbolCountText, value);
    }

    public string DependencyCountText
    {
        get => _dependencyCountText;
        set => SetField(ref _dependencyCountText, value);
    }

    public string KeyFileCountText
    {
        get => _keyFileCountText;
        set => SetField(ref _keyFileCountText, value);
    }

    public string VerificationCommandCountText
    {
        get => _verificationCommandCountText;
        set => SetField(ref _verificationCommandCountText, value);
    }

    public string ProjectMapEmptyText
    {
        get => _projectMapEmptyText;
        set => SetField(ref _projectMapEmptyText, value);
    }

    public string KeySymbolsEmptyText
    {
        get => _keySymbolsEmptyText;
        set => SetField(ref _keySymbolsEmptyText, value);
    }

    public string KeyDependenciesEmptyText
    {
        get => _keyDependenciesEmptyText;
        set => SetField(ref _keyDependenciesEmptyText, value);
    }

    public string KeyFilesEmptyText
    {
        get => _keyFilesEmptyText;
        set => SetField(ref _keyFilesEmptyText, value);
    }

    public string VerificationCommandsEmptyText
    {
        get => _verificationCommandsEmptyText;
        set => SetField(ref _verificationCommandsEmptyText, value);
    }

    public void ApplyAnalysis(WorkspaceAnalysis analysis)
    {
        AnalysisSummary = analysis.Summary;
        ProjectType = analysis.ProjectType;
        Framework = analysis.Framework;
        GitBranch = analysis.GitBranch;
        Stats = $"{analysis.FileCount:0} files / {analysis.DirectoryCount:0} folders";
        AnalysisUpdatedText = $"Updated: {analysis.UpdatedAt:HH:mm:ss}";
        DashboardSummary = BuildDashboardSummary(analysis);
        SymbolCountText = $"{analysis.SymbolCount:0} symbols";
        DependencyCountText = $"{analysis.DependencyEdgeCount:0} dependencies";
        KeyFileCountText = $"{analysis.KeyFiles.Count:0} key files";
        VerificationCommandCountText = $"{analysis.VerificationCommands.Count:0} commands";
        HealthText = BuildHealthText(analysis);
        HealthAccentBrush = PickHealthAccent(analysis);

        ReplaceItems(VerificationCommands, analysis.VerificationCommands);
        ReplaceItems(ProjectMap, analysis.ProjectMap);
        ReplaceItems(KeySymbols, analysis.KeySymbols);
        ReplaceItems(KeyDependencies, analysis.KeyDependencies);
        ReplaceItems(KeyFiles, analysis.KeyFiles);
        ReplaceItems(Hints, analysis.Hints);
        ReplaceItems(ScaffoldRecommendations, analysis.ScaffoldRecommendations);
    }

    public void ResetEmptyState()
    {
        AnalysisSummary = DesktopLocalizer.UiText(DesktopText.ProjectNotAnalyzed, UseKoreanUi);
        ProjectType = DesktopLocalizer.UiText(DesktopText.ProjectAnalyzeToDetect, UseKoreanUi);
        Framework = DesktopLocalizer.UiText(DesktopText.ProjectAnalyzeToDetect, UseKoreanUi);
        GitBranch = DesktopLocalizer.UiText(DesktopText.ProjectAnalyzeToDetect, UseKoreanUi);
        Stats = DesktopLocalizer.UiText(DesktopText.ProjectAnalyzeStats, UseKoreanUi);
        AnalysisUpdatedText = DesktopLocalizer.UiText(DesktopText.ProjectAnalysisUpdatedEmpty, UseKoreanUi);
        DashboardSummary = DesktopLocalizer.UiText(DesktopText.ProjectDashboardEmpty, UseKoreanUi);
        HealthText = DesktopLocalizer.UiText(DesktopText.ProjectWaitingForAnalysis, UseKoreanUi);
        HealthAccentBrush = "#B7C4D1";
        SymbolCountText = "0 symbols";
        DependencyCountText = "0 dependencies";
        KeyFileCountText = "0 key files";
        VerificationCommandCountText = "0 commands";
        ProjectMapEmptyText = DesktopLocalizer.UiText(DesktopText.ProjectMapEmpty, UseKoreanUi);
        KeySymbolsEmptyText = DesktopLocalizer.UiText(DesktopText.ProjectSymbolsEmpty, UseKoreanUi);
        KeyDependenciesEmptyText = DesktopLocalizer.UiText(DesktopText.ProjectDependenciesEmpty, UseKoreanUi);
        KeyFilesEmptyText = DesktopLocalizer.UiText(DesktopText.ProjectFilesEmpty, UseKoreanUi);
        VerificationCommandsEmptyText = DesktopLocalizer.UiText(DesktopText.ProjectVerificationEmpty, UseKoreanUi);
        VerificationCommands.Clear();
        ProjectMap.Clear();
        KeySymbols.Clear();
        KeyDependencies.Clear();
        KeyFiles.Clear();
        Hints.Clear();
        ScaffoldRecommendations.Clear();
    }

    private void RefreshDefaultText(bool previousUseKoreanUi)
    {
        ReplaceDefaultText(ref _analysisSummary, DesktopText.ProjectNotAnalyzed, previousUseKoreanUi, nameof(AnalysisSummary));
        ReplaceDefaultText(ref _projectType, DesktopText.ProjectAnalyzeToDetect, previousUseKoreanUi, nameof(ProjectType));
        ReplaceDefaultText(ref _framework, DesktopText.ProjectAnalyzeToDetect, previousUseKoreanUi, nameof(Framework));
        ReplaceDefaultText(ref _gitBranch, DesktopText.ProjectAnalyzeToDetect, previousUseKoreanUi, nameof(GitBranch));
        ReplaceDefaultText(ref _stats, DesktopText.ProjectAnalyzeStats, previousUseKoreanUi, nameof(Stats));
        ReplaceDefaultText(ref _analysisUpdatedText, DesktopText.ProjectAnalysisUpdatedEmpty, previousUseKoreanUi, nameof(AnalysisUpdatedText));
        ReplaceDefaultText(ref _dashboardSummary, DesktopText.ProjectDashboardEmpty, previousUseKoreanUi, nameof(DashboardSummary));
        ReplaceDefaultText(ref _healthText, DesktopText.ProjectWaitingForAnalysis, previousUseKoreanUi, nameof(HealthText));
        ReplaceDefaultText(ref _projectMapEmptyText, DesktopText.ProjectMapEmpty, previousUseKoreanUi, nameof(ProjectMapEmptyText));
        ReplaceDefaultText(ref _keySymbolsEmptyText, DesktopText.ProjectSymbolsEmpty, previousUseKoreanUi, nameof(KeySymbolsEmptyText));
        ReplaceDefaultText(ref _keyDependenciesEmptyText, DesktopText.ProjectDependenciesEmpty, previousUseKoreanUi, nameof(KeyDependenciesEmptyText));
        ReplaceDefaultText(ref _keyFilesEmptyText, DesktopText.ProjectFilesEmpty, previousUseKoreanUi, nameof(KeyFilesEmptyText));
        ReplaceDefaultText(ref _verificationCommandsEmptyText, DesktopText.ProjectVerificationEmpty, previousUseKoreanUi, nameof(VerificationCommandsEmptyText));
    }

    private void ReplaceDefaultText(ref string field, string key, bool previousUseKoreanUi, string propertyName)
    {
        if (!string.Equals(field, DesktopLocalizer.UiText(key, previousUseKoreanUi), StringComparison.Ordinal))
        {
            return;
        }

        field = DesktopLocalizer.UiText(key, UseKoreanUi);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string BuildDashboardSummary(WorkspaceAnalysis analysis)
    {
        var framework = string.IsNullOrWhiteSpace(analysis.Framework) ? "Unknown framework" : analysis.Framework;
        var type = string.IsNullOrWhiteSpace(analysis.ProjectType) ? "Unknown project" : analysis.ProjectType;
        return $"{type} workspace using {framework}. {analysis.ProjectMap.Count:0} map entries, {analysis.KeySymbols.Count:0} key symbols, {analysis.KeyFiles.Count:0} key files.";
    }

    private static string BuildHealthText(WorkspaceAnalysis analysis)
    {
        if (analysis.Hints.Any(hint => hint.Contains("Diagnostic Warning", StringComparison.OrdinalIgnoreCase)))
        {
            return "Needs environment attention";
        }

        if (analysis.VerificationCommands.Count == 0)
        {
            return "Needs verification command";
        }

        if (analysis.ProjectMap.Count == 0 || analysis.KeyFiles.Count == 0)
        {
            return "Partial map";
        }

        return "Ready";
    }

    private static string PickHealthAccent(WorkspaceAnalysis analysis)
    {
        if (analysis.Hints.Any(hint => hint.Contains("Diagnostic Warning", StringComparison.OrdinalIgnoreCase)))
        {
            return "#FBBF24";
        }

        if (analysis.VerificationCommands.Count == 0 || analysis.ProjectMap.Count == 0)
        {
            return "#FBBF24";
        }

        return "#37D67A";
    }

    private static void ReplaceItems(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
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
}
