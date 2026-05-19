using System.Windows;
using AgentQ.Desktop.Services;

namespace AgentQ.Desktop.Views;

public partial class VerificationPanel : System.Windows.Controls.UserControl
{
    public VerificationPanel()
    {
        InitializeComponent();
    }

    public event EventHandler<AgentVerificationPlan>? RunRequested;

    public event EventHandler? FixFailureRequested;

    public event EventHandler? AutoFixRequested;

    private void Run_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AgentVerificationPlan plan } &&
            !string.IsNullOrWhiteSpace(plan.Command))
        {
            RunRequested?.Invoke(this, plan);
        }
    }

    private void FixFailure_OnClick(object sender, RoutedEventArgs e)
    {
        FixFailureRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AutoFix_OnClick(object sender, RoutedEventArgs e)
    {
        AutoFixRequested?.Invoke(this, EventArgs.Empty);
    }
}
