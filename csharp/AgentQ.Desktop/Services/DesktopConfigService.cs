using System.IO;
using System.Text.Json;
using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public sealed class DesktopConfigService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

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
            return JsonSerializer.Deserialize<ProviderConfiguration>(json, Options);
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
        var json = JsonSerializer.Serialize(config, Options);

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
