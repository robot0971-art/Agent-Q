using System.Windows;

namespace AgentQ.Desktop.Views;

public partial class EvalReplayDashboardPanel : System.Windows.Controls.UserControl
{
    public EvalReplayDashboardPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? RefreshRequested;

    private void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }
}
