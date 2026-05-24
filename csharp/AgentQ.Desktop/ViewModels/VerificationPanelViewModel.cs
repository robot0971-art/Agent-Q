using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.ViewModels;

public sealed class VerificationPanelViewModel : INotifyPropertyChanged
{
    private bool _canFixLastFailure;
    private string _lastFailureSummary = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AgentVerificationPlan> Plans { get; } = [];

    public ObservableCollection<VerificationResultCard> Results { get; } = [];

    public bool CanFixLastFailure
    {
        get => _canFixLastFailure;
        set => SetField(ref _canFixLastFailure, value);
    }

    public string LastFailureSummary
    {
        get => _lastFailureSummary;
        set => SetField(ref _lastFailureSummary, value);
    }

    public void SetLastFailure(string summary)
    {
        LastFailureSummary = summary;
        CanFixLastFailure = true;
    }

    public void ClearLastFailure()
    {
        LastFailureSummary = string.Empty;
        CanFixLastFailure = false;
    }

    public void AddResult(VerificationResultCard result)
    {
        Results.Insert(0, result);
        while (Results.Count > 8)
        {
            Results.RemoveAt(Results.Count - 1);
        }
    }

    public void Clear()
    {
        Plans.Clear();
        Results.Clear();
        ClearLastFailure();
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
