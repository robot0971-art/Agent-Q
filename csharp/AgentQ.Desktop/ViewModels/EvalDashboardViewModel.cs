using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class EvalDashboardViewModel : INotifyPropertyChanged
{
    private bool _useKoreanUi;
    private string _summary = DesktopLocalizer.UiText(DesktopText.EvalDashboardEmpty, useKoreanUi: false);
    private string _updatedText = DesktopLocalizer.UiText(DesktopText.EvalWaitingForRefresh, useKoreanUi: false);

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Metrics { get; } = [];

    public ObservableCollection<string> Findings { get; } = [];

    public ObservableCollection<string> ReplayEntries { get; } = [];

    public ObservableCollection<string> FailureFingerprints { get; } = [];

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
        Summary = DesktopLocalizer.UiText(DesktopText.EvalDashboardEmpty, UseKoreanUi);
        UpdatedText = DesktopLocalizer.UiText(DesktopText.EvalWaitingForRefresh, UseKoreanUi);
    }

    private void RefreshDefaultText(bool previousUseKoreanUi)
    {
        ReplaceDefaultText(ref _summary, DesktopText.EvalDashboardEmpty, previousUseKoreanUi, nameof(Summary));
        ReplaceDefaultText(ref _updatedText, DesktopText.EvalWaitingForRefresh, previousUseKoreanUi, nameof(UpdatedText));
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
