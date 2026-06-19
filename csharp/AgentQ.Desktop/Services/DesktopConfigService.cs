using System.IO;
using System.Text.Json;
using AgentQ.Api;
using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public sealed class DesktopConfigService
{
    private static readonly JsonSerializerOptions Options = AgentQJsonOptions.CaseInsensitiveIndented;

    private readonly string _configDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".agentq");

    public string ConfigPath => Path.Combine(_configDirectory, "config.json");

    public async Task<ProviderConfiguration?> LoadAsync()
    {
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
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(ProviderConfiguration config)
    {
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
