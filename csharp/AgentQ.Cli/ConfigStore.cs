using System.Text.Json;
using AgentQ.Core.Providers;

namespace AgentQ.Cli;

public interface IConfigStore
{
    string PathValue { get; }

    bool Exists { get; }

    Task SaveAsync(ProviderConfiguration config);

    Task<ProviderConfiguration?> LoadAsync();

    void Delete();
}

public sealed class FileConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static FileConfigStore Default { get; } = new();

    public string PathValue => GetConfigPath();

    public bool Exists => File.Exists(GetConfigPath());

    public async Task SaveAsync(ProviderConfiguration config)
    {
        var configDirectory = GetConfigDirectory();
        var configPath = GetConfigPath();

        if (!Directory.Exists(configDirectory))
        {
            Directory.CreateDirectory(configDirectory);
        }

        var json = JsonSerializer.Serialize(config, Options);
        var tempPath = Path.Combine(configDirectory, $"config.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, json);

            if (File.Exists(configPath))
            {
                File.Replace(tempPath, configPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, configPath);
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

    public async Task<ProviderConfiguration?> LoadAsync()
    {
        var configPath = GetConfigPath();

        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            return JsonSerializer.Deserialize<ProviderConfiguration>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    public void Delete()
    {
        var configPath = GetConfigPath();

        if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }
    }

    private static string GetConfigDirectory()
    {
        var homeDirectory = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(homeDirectory, ".agentq");
    }

    private static string GetConfigPath()
    {
        return Path.Combine(GetConfigDirectory(), "config.json");
    }
}

public static class ConfigStore
{
    public static string PathValue => FileConfigStore.Default.PathValue;

    public static bool Exists => FileConfigStore.Default.Exists;

    public static Task SaveAsync(ProviderConfiguration config) => FileConfigStore.Default.SaveAsync(config);

    public static Task<ProviderConfiguration?> LoadAsync() => FileConfigStore.Default.LoadAsync();

    public static void Delete() => FileConfigStore.Default.Delete();
}
