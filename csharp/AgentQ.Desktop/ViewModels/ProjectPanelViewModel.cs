using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class ProjectPanelViewModel : INotifyPropertyChanged
{
    private string _analysisSummary = "Workspace not analyzed yet.";
    private string _projectType = "Unknown";
    private string _framework = "Unknown";
    private string _gitBranch = "Unknown";
    private string _stats = "No stats yet.";
    private string _analysisUpdatedText = "Not analyzed yet.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> VerificationCommands { get; } = [];

    public ObservableCollection<string> ProjectMap { get; } = [];

    public ObservableCollection<string> KeySymbols { get; } = [];

    public ObservableCollection<string> KeyDependencies { get; } = [];

    public ObservableCollection<string> KeyFiles { get; } = [];

    public ObservableCollection<string> Hints { get; } = [];

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

    public void ApplyAnalysis(WorkspaceAnalysis analysis)
    {
        AnalysisSummary = analysis.Summary;
        ProjectType = analysis.ProjectType;
        Framework = analysis.Framework;
        GitBranch = analysis.GitBranch;
        Stats = $"{analysis.FileCount:0} files / {analysis.DirectoryCount:0} folders";
        AnalysisUpdatedText = $"Updated: {analysis.UpdatedAt:HH:mm:ss}";

        ReplaceItems(VerificationCommands, analysis.VerificationCommands);
        ReplaceItems(ProjectMap, analysis.ProjectMap);
        ReplaceItems(KeySymbols, analysis.KeySymbols);
        ReplaceItems(KeyDependencies, analysis.KeyDependencies);
        ReplaceItems(KeyFiles, analysis.KeyFiles);
        ReplaceItems(Hints, analysis.Hints);
    }

    private static void ReplaceItems(ObservableCollection<string> target, IEnumerable<string> values)
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
