using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Core.Providers;

namespace AgentQ.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _provider = "opencode-go";
    private string _model = "kimi-k2.6";
    private string _baseUrl = ProviderConfiguration.OpenCodeGoDefaultBaseUrl;
    private string _apiKey = string.Empty;
    private string _workspaceRoot = Environment.CurrentDirectory;
    private string _inputText = string.Empty;
    private string _statusText = "준비됨";
    private int _timeoutSeconds;
    private uint _maxTokens = 4096;
    private double _desktopFontSize = 14;
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<string> Logs { get; } = [];

    public ObservableCollection<string> Attachments { get; } = [];

    public string Provider
    {
        get => _provider;
        set => SetField(ref _provider, value);
    }

    public string Model
    {
        get => _model;
        set => SetField(ref _model, value);
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set => SetField(ref _baseUrl, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetField(ref _apiKey, value);
    }

    public string WorkspaceRoot
    {
        get => _workspaceRoot;
        set => SetField(ref _workspaceRoot, value);
    }

    public string InputText
    {
        get => _inputText;
        set => SetField(ref _inputText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => SetField(ref _timeoutSeconds, value);
    }

    public uint MaxTokens
    {
        get => _maxTokens;
        set => SetField(ref _maxTokens, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public double DesktopFontSize
    {
        get => _desktopFontSize;
        set => SetField(ref _desktopFontSize, Math.Clamp(value, 11, 22));
    }

    public ProviderConfiguration ToConfiguration()
    {
        return new ProviderConfiguration
        {
            Provider = Provider,
            Model = Model,
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            TimeoutSeconds = TimeoutSeconds,
            MaxTokens = MaxTokens,
            DesktopFontSize = DesktopFontSize
        };
    }

    public void ApplyConfiguration(ProviderConfiguration config)
    {
        Provider = string.IsNullOrWhiteSpace(config.Provider) ? "opencode-go" : config.Provider;
        Model = string.IsNullOrWhiteSpace(config.Model) ? "kimi-k2.6" : config.Model;
        BaseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? ProviderConfiguration.OpenCodeGoDefaultBaseUrl : config.BaseUrl;
        ApiKey = config.ApiKey;
        TimeoutSeconds = config.TimeoutSeconds;
        MaxTokens = config.MaxTokens == 0 ? 4096 : config.MaxTokens;
        DesktopFontSize = config.DesktopFontSize <= 0 ? 14 : config.DesktopFontSize;
    }

    public void AddLog(string message)
    {
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
