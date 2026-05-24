using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class EvalDashboardViewModel : INotifyPropertyChanged
{
    private string _summary = "Eval dashboard not loaded.";
    private string _updatedText = "Not refreshed yet.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Metrics { get; } = [];

    public ObservableCollection<string> Findings { get; } = [];

    public ObservableCollection<string> ReplayEntries { get; } = [];

    public ObservableCollection<string> FailureFingerprints { get; } = [];

    public string Summary
    {
        get => _summary;
        set => SetField(ref _summary, value);
    }

    public string UpdatedText
    {
        get => _updatedText;
        set => SetField(ref _updatedText, value);
    }

    public void ApplyReport(EvalReplayDashboardReport report)
    {
        Summary = report.Summary;
        UpdatedText = $"Updated: {report.UpdatedAt:HH:mm:ss}";

        ReplaceItems(Metrics, report.Metrics);
        ReplaceItems(Findings, report.Findings);
        ReplaceItems(ReplayEntries, report.ReplayEntries);
        ReplaceItems(FailureFingerprints, report.FailureFingerprints);
    }

    public void Reset()
    {
        Metrics.Clear();
        Findings.Clear();
        ReplayEntries.Clear();
        FailureFingerprints.Clear();
        Summary = "Eval dashboard not loaded.";
        UpdatedText = "Not refreshed yet.";
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
