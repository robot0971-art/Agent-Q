using System.Text.Json;
using System.Text.Json.Serialization;
using AgentQ.Core.Models;

namespace AgentQ.Cli;

/// <summary>
/// 대화 세션 영구 저장소
/// </summary>
public static class SessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 세션을 파일로 저장
    /// </summary>
    /// <param name="filePath">저장할 파일 경로</param>
    /// <param name="messages">메시지 목록</param>
    public static async Task SaveAsync(string filePath, IEnumerable<ChatMessage> messages)
    {
        var json = JsonSerializer.Serialize(messages, Options);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// 파일에서 세션 로드
    /// </summary>
    /// <param name="filePath">불러올 파일 경로</param>
    /// <returns>메시지 목록</returns>
    public static async Task<List<ChatMessage>> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Session file not found: {filePath}");
        }

        var json = await File.ReadAllTextAsync(filePath);
        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(json, Options);
        return messages ?? new List<ChatMessage>();
    }
}
