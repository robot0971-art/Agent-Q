using System.Windows;

namespace AgentQ.Desktop.Views;

public partial class SettingsPanel : System.Windows.Controls.UserControl
{
    private bool _isSettingApiKey;

    public SettingsPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? SaveRequested;

    public event EventHandler<string>? ApiKeyChanged;

    public string ApiKey
    {
        get => ApiKeyBox.Password;
        set
        {
            _isSettingApiKey = true;
            ApiKeyBox.Password = value ?? string.Empty;
            _isSettingApiKey = false;
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSettingApiKey)
        {
            return;
        }

        ApiKeyChanged?.Invoke(this, ApiKeyBox.Password);
    }
}
