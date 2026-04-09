using System.Text.Json;
using AgentQ.Core.Providers;

namespace AgentQ.Cli;

/// <summary>
/// 사용자 설정 파일 저장소입니다.
/// </summary>
public static class ConfigStore
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".agentq");

    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 설정 파일 전체 경로입니다.
    /// </summary>
    public static string PathValue => ConfigPath;

    /// <summary>
    /// 설정을 파일로 저장합니다.
    /// </summary>
    /// <param name="config">저장할 설정 객체</param>
    public static async Task SaveAsync(ProviderConfiguration config)
    {
        if (!Directory.Exists(ConfigDirectory))
        {
            Directory.CreateDirectory(ConfigDirectory);
        }

        var json = JsonSerializer.Serialize(config, Options);
        await File.WriteAllTextAsync(ConfigPath, json);
    }

    /// <summary>
    /// 설정 파일에서 설정을 불러옵니다.
    /// </summary>
    /// <returns>불러온 설정 객체 또는 null</returns>
    public static async Task<ProviderConfiguration?> LoadAsync()
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

    /// <summary>
    /// 저장된 설정 파일을 삭제합니다.
    /// </summary>
    public static void Delete()
    {
        if (File.Exists(ConfigPath))
        {
            File.Delete(ConfigPath);
        }
    }

    /// <summary>
    /// 설정 파일 존재 여부입니다.
    /// </summary>
    public static bool Exists => File.Exists(ConfigPath);
}
