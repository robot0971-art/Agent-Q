using System.Text.Json;
using System.Text.Json.Serialization;
using AgentQ.Core.Models;

namespace AgentQ.Cli;

public interface ISessionStore
{
    Task SaveAsync(string filePath, IEnumerable<ChatMessage> messages);

    Task<List<ChatMessage>> LoadAsync(string filePath);
}

public sealed class FileSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static FileSessionStore Default { get; } = new();

    public async Task SaveAsync(string filePath, IEnumerable<ChatMessage> messages)
    {
        var json = JsonSerializer.Serialize(messages, Options);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<List<ChatMessage>> LoadAsync(string filePath)
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

public static class SessionStore
{
    public static Task SaveAsync(string filePath, IEnumerable<ChatMessage> messages) =>
        FileSessionStore.Default.SaveAsync(filePath, messages);

    public static Task<List<ChatMessage>> LoadAsync(string filePath) =>
        FileSessionStore.Default.LoadAsync(filePath);
}
