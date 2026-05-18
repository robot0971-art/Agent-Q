using System.Text.Json;
using AgentQ.Core.Providers;

namespace AgentQ.Cli;

/// <summary>
/// 사용자 설정 파일 저장소입니다.
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 설정 파일 전체 경로입니다.
    /// </summary>
    public static string PathValue => GetConfigPath();

    /// <summary>
    /// 설정을 파일로 저장합니다.
    /// </summary>
    /// <param name="config">저장할 설정 객체</param>
    public static async Task SaveAsync(ProviderConfiguration config)
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

    /// <summary>
    /// 설정 파일에서 설정을 불러옵니다.
    /// </summary>
    /// <returns>불러온 설정 객체 또는 null</returns>
    public static async Task<ProviderConfiguration?> LoadAsync()
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

    /// <summary>
    /// 저장된 설정 파일을 삭제합니다.
    /// </summary>
    public static void Delete()
    {
        var configPath = GetConfigPath();

        if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }
    }

    /// <summary>
    /// 설정 파일 존재 여부입니다.
    /// </summary>
    public static bool Exists => File.Exists(GetConfigPath());

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
