using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using AgentQ.Api;
using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public sealed class DesktopProviderModelDiscoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = AgentQJsonOptions.CaseInsensitiveIndented;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _cacheDirectory;

    public DesktopProviderModelDiscoveryService(IHttpClientFactory httpClientFactory)
        : this(
            httpClientFactory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agentq", "model-cache"))
    {
    }

    public DesktopProviderModelDiscoveryService(IHttpClientFactory httpClientFactory, string cacheDirectory)
    {
        _httpClientFactory = httpClientFactory;
        _cacheDirectory = cacheDirectory;
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(
        ProviderConfiguration config,
        CancellationToken cancellationToken = default)
    {
        var provider = NormalizeProvider(config.Provider);
        var fallback = DesktopProviderModelCatalog.GetModels(provider);

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return await TryReadCacheAsync(provider, config.BaseUrl, cancellationToken) ?? fallback;
        }

        try
        {
            var discovered = await FetchModelsAsync(provider, config.BaseUrl, config.ApiKey, cancellationToken);
            if (discovered.Count == 0)
            {
                return await TryReadCacheAsync(provider, config.BaseUrl, cancellationToken) ?? fallback;
            }

            await WriteCacheAsync(provider, config.BaseUrl, discovered, cancellationToken);
            return MergeWithFallback(discovered, fallback);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await TryReadCacheAsync(provider, config.BaseUrl, cancellationToken) ?? fallback;
        }
    }

    private async Task<IReadOnlyList<string>> FetchModelsAsync(
        string provider,
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var request = CreateRequest(provider, baseUrl, apiKey);
        if (request == null)
        {
            return [];
        }

        using var client = _httpClientFactory.CreateClient("model-discovery");
        client.Timeout = TimeSpan.FromSeconds(15);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return ExtractModelIds(document.RootElement)
            .Where(id => IsUsableChatModel(provider, id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => GetModelSortRank(provider, id))
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HttpRequestMessage? CreateRequest(string provider, string baseUrl, string apiKey)
    {
        return provider switch
        {
            "anthropic" => CreateAnthropicRequest(baseUrl, apiKey),
            "google" => CreateGoogleRequest(apiKey),
            "openai" or "opencode-go" or "xai" or "deepseek" => CreateOpenAiCompatibleRequest(baseUrl, apiKey),
            _ => null
        };
    }

    private static HttpRequestMessage CreateOpenAiCompatibleRequest(string baseUrl, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, CombineUrl(baseUrl, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static HttpRequestMessage CreateAnthropicRequest(string baseUrl, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, CombineUrl(baseUrl, "v1/models"));
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        return request;
    }

    private static HttpRequestMessage CreateGoogleRequest(string apiKey)
    {
        return new HttpRequestMessage(
            HttpMethod.Get,
            $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}");
    }

    private static IEnumerable<string> ExtractModelIds(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = ReadStringProperty(item, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    yield return id;
                }
            }
        }

        if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in models.EnumerateArray())
            {
                var name = ReadStringProperty(item, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    yield return name.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                        ? name["models/".Length..]
                        : name;
                }
            }
        }
    }

    private static string? ReadStringProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool IsUsableChatModel(string provider, string id)
    {
        var lower = id.ToLowerInvariant();
        if (lower.Contains("embedding", StringComparison.Ordinal) ||
            lower.Contains("moderation", StringComparison.Ordinal) ||
            lower.Contains("whisper", StringComparison.Ordinal) ||
            lower.Contains("tts", StringComparison.Ordinal) ||
            lower.Contains("audio", StringComparison.Ordinal) ||
            lower.Contains("image", StringComparison.Ordinal) ||
            lower.Contains("realtime", StringComparison.Ordinal) ||
            lower.Contains("transcribe", StringComparison.Ordinal))
        {
            return false;
        }

        return provider switch
        {
            "google" => lower.StartsWith("gemini-", StringComparison.Ordinal),
            "anthropic" => lower.StartsWith("claude-", StringComparison.Ordinal),
            "openai" => lower.StartsWith("gpt-", StringComparison.Ordinal) || lower.StartsWith("o", StringComparison.Ordinal),
            _ => true
        };
    }

    private static int GetModelSortRank(string provider, string id)
    {
        var lower = id.ToLowerInvariant();
        return provider switch
        {
            "google" when lower.StartsWith("gemini-3", StringComparison.Ordinal) => 0,
            "google" when lower.StartsWith("gemini-2.5", StringComparison.Ordinal) => 1,
            "openai" when lower.StartsWith("gpt-5", StringComparison.Ordinal) => 0,
            "anthropic" when lower.Contains("opus-4", StringComparison.Ordinal) => 0,
            "anthropic" when lower.Contains("sonnet-4", StringComparison.Ordinal) => 1,
            "xai" when lower.StartsWith("grok-4", StringComparison.Ordinal) => 0,
            "deepseek" when lower.StartsWith("deepseek-v4", StringComparison.Ordinal) => 0,
            _ => 10
        };
    }

    private static IReadOnlyList<string> MergeWithFallback(
        IReadOnlyList<string> discovered,
        IReadOnlyList<string> fallback)
    {
        return discovered
            .Concat(fallback)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<string>?> TryReadCacheAsync(
        string provider,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var path = GetCachePath(provider, baseUrl);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var cache = await JsonSerializer.DeserializeAsync<ModelCacheEntry>(stream, JsonOptions, cancellationToken);
            if (cache == null || DateTimeOffset.UtcNow - cache.FetchedAt > CacheTtl || cache.Models.Count == 0)
            {
                return null;
            }

            return cache.Models;
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(
        string provider,
        string baseUrl,
        IReadOnlyList<string> models,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var entry = new ModelCacheEntry(DateTimeOffset.UtcNow, models.ToList());
        var path = GetCachePath(provider, baseUrl);
        var tempPath = Path.Combine(_cacheDirectory, $"{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
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

    private string GetCachePath(string provider, string baseUrl)
    {
        var cacheKey = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{provider}|{baseUrl}")))[..16];
        return Path.Combine(_cacheDirectory, $"{provider}.{cacheKey}.json");
    }

    private static string NormalizeProvider(string provider)
    {
        return string.IsNullOrWhiteSpace(provider) ? "opencode-go" : provider.Trim().ToLowerInvariant();
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1" : baseUrl.TrimEnd('/');
        return root.EndsWith($"/{path}", StringComparison.OrdinalIgnoreCase)
            ? root
            : $"{root}/{path}";
    }

    private sealed record ModelCacheEntry(DateTimeOffset FetchedAt, List<string> Models);
}
