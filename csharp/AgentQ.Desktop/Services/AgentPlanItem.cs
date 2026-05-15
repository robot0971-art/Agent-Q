using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AgentQ.Desktop.Services;

public sealed class AgentPlanItem : INotifyPropertyChanged
{
    private AgentPlanItemStatus _status = AgentPlanItemStatus.Pending;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Order { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public AgentPlanItemStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => Status.ToString();

    public string DisplayTitle => $"{Order}. {Title}";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
