using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentQ.Providers.OpenAi;

namespace AgentQ.Desktop.Services;

public sealed class OpenAiEmbeddingClient(HttpClient httpClient) : IEmbeddingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static HttpClient CreateHttpClient(string baseUrl, string apiKey)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(OpenAiCompatibleProvider.NormalizeBaseUrl(baseUrl))
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return client;
    }

    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        string model,
        CancellationToken ct = default)
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Embedding model must not be empty.", nameof(model));
        }

        var request = new OpenAiEmbeddingRequest
        {
            Model = model,
            Input = inputs.ToList()
        };
        var json = JsonSerializer.Serialize(request, JsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync("embeddings", content, ct);
        await EnsureSuccessStatusCodeAsync(response, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var embeddingResponse = JsonSerializer.Deserialize<OpenAiEmbeddingResponse>(body, JsonOptions) ??
            throw new InvalidOperationException("OpenAI embedding response was empty.");

        return embeddingResponse.Data
            .OrderBy(item => item.Index)
            .Select(item => item.Embedding)
            .ToList();
    }

    private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var trimmedBody = string.IsNullOrWhiteSpace(body)
            ? "<empty body>"
            : body.Length > 512
                ? body[..512]
                : body;

        throw new HttpRequestException(
            $"OpenAI embedding request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {trimmedBody}",
            null,
            response.StatusCode);
    }
}

public sealed class OpenAiEmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public List<string> Input { get; set; } = [];
}

public sealed class OpenAiEmbeddingResponse
{
    [JsonPropertyName("data")]
    public List<OpenAiEmbeddingData> Data { get; set; } = [];
}

public sealed class OpenAiEmbeddingData
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];
}
