using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentQ.Core.Providers;

namespace AgentQ.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly Dictionary<string, string[]> ModelCatalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["opencode-go"] =
        [
            "kimi-k2.6",
            "kimi-k2.5",
            "deepseek-v4-pro",
            "deepseek-v4-flash",
            "glm-5.1",
            "glm-5",
            "mimo-v2.5-pro",
            "mimo-v2.5"
        ],
        ["openai"] =
        [
            "gpt-5.2",
            "gpt-5.1",
            "gpt-4.1",
            "gpt-4.1-mini",
            "gpt-4o",
            "gpt-4o-mini",
            "o3",
            "o4-mini"
        ],
        ["anthropic"] =
        [
            "claude-opus-4-1",
            "claude-opus-4",
            "claude-sonnet-4-5",
            "claude-sonnet-4",
            "claude-3-7-sonnet-latest",
            "claude-3-5-haiku-latest"
        ],
        ["google"] =
        [
            "gemini-2.5-pro",
            "gemini-2.5-flash",
            "gemini-2.0-flash"
        ],
        ["xai"] =
        [
            "grok-4",
            "grok-3",
            "grok-3-mini"
        ],
        ["deepseek"] =
        [
            "deepseek-chat",
            "deepseek-reasoner"
        ]
    };

    private string _provider = "opencode-go";
    private string _model = "kimi-k2.6";
    private string _baseUrl = ProviderConfiguration.OpenCodeGoDefaultBaseUrl;
    private string _apiKey = string.Empty;
    private string _workspaceRoot = Environment.CurrentDirectory;
    private string _inputText = string.Empty;
    private string _statusText = "Ready";
    private int _timeoutSeconds;
    private uint _maxTokens = 4096;
    private double _desktopFontSize = 14;
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<string> Logs { get; } = [];

    public ObservableCollection<string> Attachments { get; } = [];

    public ObservableCollection<string> AvailableProviders { get; } = new(ModelCatalog.Keys);

    public ObservableCollection<string> AvailableModels { get; } = new(ModelCatalog["opencode-go"]);

    public string Provider
    {
        get => _provider;
        set
        {
            if (!SetField(ref _provider, value))
            {
                return;
            }

            RefreshModelsForProvider(preserveCurrentModel: false);
            ApplyProviderDefaults();
        }
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
        _provider = string.IsNullOrWhiteSpace(config.Provider) ? "opencode-go" : config.Provider;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Provider)));
        RefreshModelsForProvider(preserveCurrentModel: true);
        Model = string.IsNullOrWhiteSpace(config.Model) ? GetDefaultModel(Provider) : config.Model;
        BaseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? ProviderConfiguration.OpenCodeGoDefaultBaseUrl : config.BaseUrl;
        ApiKey = config.ApiKey;
        TimeoutSeconds = config.TimeoutSeconds;
        MaxTokens = config.MaxTokens == 0 ? 4096 : config.MaxTokens;
        DesktopFontSize = config.DesktopFontSize <= 0 ? 14 : config.DesktopFontSize;
    }

    public void AddLog(string message)
    {
        Logs.Add($"{DateTime.Now:HH:mm:ss}  INFO  {message}");
    }

    private void RefreshModelsForProvider(bool preserveCurrentModel)
    {
        var currentModel = Model;
        AvailableModels.Clear();

        var models = ModelCatalog.TryGetValue(Provider, out var catalogModels)
            ? catalogModels
            : ["default"];

        foreach (var model in models)
        {
            AvailableModels.Add(model);
        }

        if (preserveCurrentModel && !string.IsNullOrWhiteSpace(currentModel))
        {
            Model = currentModel;
            return;
        }

        Model = models[0];
    }

    private void ApplyProviderDefaults()
    {
        BaseUrl = Provider.ToLowerInvariant() switch
        {
            "opencode-go" => ProviderConfiguration.OpenCodeGoDefaultBaseUrl,
            "openai" => "https://api.openai.com/v1",
            "anthropic" => "https://api.anthropic.com",
            "google" => "https://generativelanguage.googleapis.com/v1beta/openai",
            "xai" => "https://api.x.ai/v1",
            "deepseek" => "https://api.deepseek.com",
            _ => BaseUrl
        };
    }

    private static string GetDefaultModel(string provider)
    {
        return ModelCatalog.TryGetValue(provider, out var models) ? models[0] : "default";
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
