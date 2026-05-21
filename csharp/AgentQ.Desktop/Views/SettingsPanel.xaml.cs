using System.Windows;

namespace AgentQ.Desktop.Views;

public partial class SettingsPanel : System.Windows.Controls.UserControl
{
    private bool _isSettingApiKey;
    private bool _isSettingEmbeddingApiKey;

    public SettingsPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? SaveRequested;

    public event EventHandler<string>? ApiKeyChanged;

    public event EventHandler<string>? EmbeddingApiKeyChanged;

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

    public string EmbeddingApiKey
    {
        get => EmbeddingApiKeyBox.Password;
        set
        {
            _isSettingEmbeddingApiKey = true;
            EmbeddingApiKeyBox.Password = value ?? string.Empty;
            _isSettingEmbeddingApiKey = false;
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

    private void EmbeddingApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSettingEmbeddingApiKey)
        {
            return;
        }

        EmbeddingApiKeyChanged?.Invoke(this, EmbeddingApiKeyBox.Password);
    }
}
