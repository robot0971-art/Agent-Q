using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentQ.Tools;

public sealed class WebSearchTool(HttpClient? httpClient = null) : ITool
{
    private const int DefaultMaxResults = 5;
    private const int MaximumMaxResults = 10;
    private const int MaximumQueryLength = 500;
    private const int MaximumTitleLength = 180;
    private const int MaximumUrlLength = 2048;
    private const int MaximumSnippetLength = 500;
    private static readonly Regex ResultRegex = new(
        "<a[^>]+class=\"result__a\"[^>]+href=\"(?<url>[^\"]+)\"[^>]*>(?<title>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex SnippetRegex = new(
        "<a[^>]+class=\"result__snippet\"[^>]*>(?<snippet>.*?)</a>|<div[^>]+class=\"result__snippet\"[^>]*>(?<snippet>.*?)</div>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private readonly HttpClient _httpClient = httpClient ?? CreateDefaultHttpClient();

    public string Name => "web_search";

    public string Description =>
        "Search the public web for current information and return evidence results with title, URL, and snippet. Use for requests like 'find and summarize'; do not invent findings when this returns no results.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Web search query" },
            max_results = new { type = "integer", description = "Maximum results to return, 1-10. Default 5." }
        },
        required = new[] { "query" }
    };

    public bool RequiresPermission => true;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default)
    {
        var query = ToolInputParser.GetString(input, "query", fallbackToString: true);
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Error("Missing required parameter: query");
        }

        query = query.Trim();
        if (query.Length > MaximumQueryLength)
        {
            return ToolResult.Error($"query is too long; keep it under {MaximumQueryLength} characters and search the current request, not pasted logs.");
        }

        var maxResults = Math.Clamp(ToolInputParser.GetInt32(input, "max_results") ?? DefaultMaxResults, 1, MaximumMaxResults);
        try
        {
            var url = "https://duckduckgo.com/html/?q=" + Uri.EscapeDataString(query);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("AgentQ/1.0");
            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(ct);
            var results = ParseResults(html, maxResults);
            var payload = new
            {
                query,
                source = "duckduckgo_html",
                resultCount = results.Count,
                results
            };
            return ToolResult.Success(JsonSerializer.Serialize(payload));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return ToolResult.Error($"Web search failed: {ex.Message}");
        }
    }

    private static List<WebSearchResult> ParseResults(string html, int maxResults)
    {
        var titleMatches = ResultRegex.Matches(html);
        var snippetMatches = SnippetRegex.Matches(html);
        var results = new List<WebSearchResult>();

        for (var index = 0; index < titleMatches.Count && results.Count < maxResults; index++)
        {
            var titleMatch = titleMatches[index];
            var title = Truncate(CleanHtml(titleMatch.Groups["title"].Value), MaximumTitleLength);
            var url = Truncate(DecodeDuckDuckGoUrl(CleanHtml(titleMatch.Groups["url"].Value)), MaximumUrlLength);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var snippet = index < snippetMatches.Count
                ? Truncate(CleanHtml(snippetMatches[index].Groups["snippet"].Value), MaximumSnippetLength)
                : string.Empty;
            results.Add(new WebSearchResult(title, url, snippet));
        }

        return results;
    }

    private static string DecodeDuckDuckGoUrl(string url)
    {
        var decoded = WebUtility.HtmlDecode(url);
        if (!Uri.TryCreate(decoded, UriKind.Absolute, out var uri))
        {
            return decoded;
        }

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && string.Equals(pair[0], "uddg", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return decoded;
    }

    private static string CleanHtml(string value)
    {
        var withoutTags = Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].TrimEnd() + "...";
    }

    private static HttpClient CreateDefaultHttpClient() =>
        new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

    private sealed record WebSearchResult(
        string title,
        string url,
        string snippet);
}
