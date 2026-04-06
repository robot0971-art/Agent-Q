using System.Text.Json;
using AgentQ.Core.Providers;

namespace AgentQ.Cli;

/// <summary>
/// 에이전트 설정 영구 저장소
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
    /// 설정을 파일로 저장
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
    /// 파일에서 설정 로드
    /// </summary>
    /// <returns>로드된 설정 객체 또는 null</returns>
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
    /// 설정 파일 존재 여부 확인
    /// </summary>
    public static bool Exists => File.Exists(ConfigPath);
}
