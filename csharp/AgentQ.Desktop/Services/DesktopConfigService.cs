using System.IO;
using System.Text.Json;
using AgentQ.Api;
using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public sealed class DesktopConfigService
{
    private static readonly JsonSerializerOptions Options = AgentQJsonOptions.CaseInsensitiveIndented;

    private readonly string _configDirectory;

    public DesktopConfigService(string? configDirectory = null)
    {
        _configDirectory = string.IsNullOrWhiteSpace(configDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".agentq")
            : Path.GetFullPath(configDirectory);
    }

    public string ConfigPath => Path.Combine(_configDirectory, "config.json");

    public string? LastLoadError { get; private set; }

    public async Task<ProviderConfiguration?> LoadAsync()
    {
        LastLoadError = null;
        if (!File.Exists(ConfigPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(ConfigPath);
            var config = JsonSerializer.Deserialize<ProviderConfiguration>(json, Options);
            return config == null ? null : ProviderConfigurationSecrets.UnprotectFromStorage(config);
        }
        catch (Exception ex)
        {
            LastLoadError = ex.Message;
            return null;
        }
    }

    public async Task SaveAsync(ProviderConfiguration config)
    {
        LastLoadError = null;
        Directory.CreateDirectory(_configDirectory);
        var tempPath = Path.Combine(_configDirectory, $"config.desktop.{Guid.NewGuid():N}.tmp");
        var storageConfig = ProviderConfigurationSecrets.ProtectForStorage(config);
        var json = JsonSerializer.Serialize(storageConfig, Options);

        try
        {
            await File.WriteAllTextAsync(tempPath, json);
            if (File.Exists(ConfigPath))
            {
                File.Replace(tempPath, ConfigPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, ConfigPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
