using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public sealed class LinkContentFetcher
{
    private static readonly Regex UrlRegex = new(@"https?://[^\s<>""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ScriptStyleRegex = new(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;

    public LinkContentFetcher(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> BuildContextAsync(string text, CancellationToken ct)
    {
        var urls = UrlRegex.Matches(text)
            .Select(match => match.Value.TrimEnd('.', ',', ')', ']', '}'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (urls.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Linked page context:");
        builder.AppendLine("Link auto-read is enabled. AgentQ attempted to fetch the URL(s) below.");
        builder.AppendLine("Use successful fetches as evidence. If a fetch failed, mention the failure reason and ask for pasted text or a local file only as a fallback.");

        foreach (var url in urls)
        {
            builder.AppendLine();
            builder.AppendLine($"URL: {url}");
            builder.AppendLine(await FetchSummaryAsync(url, ct));
        }

        return builder.ToString().Trim();
    }

    private async Task<string> FetchSummaryAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("AgentQ-Desktop/1.0");
            request.Headers.Accept.ParseAdd("text/html, text/plain;q=0.9, */*;q=0.1");

            var httpClient = _httpClientFactory.CreateClient("desktop-links");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return $"Fetch failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.";
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Contains("text", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(mediaType)
                    ? "Fetch failed: unsupported or missing content type."
                    : $"Fetch failed: unsupported content type {mediaType}.";
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var plainText = mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                ? HtmlToText(content)
                : content;

            plainText = WhitespaceRegex.Replace(plainText, " ").Trim();
            if (plainText.Length > 6000)
            {
                plainText = plainText[..6000] + "...";
            }

            return string.IsNullOrWhiteSpace(plainText)
                ? "Fetch failed: no readable text found."
                : $"Fetch succeeded. Readable text excerpt: {plainText}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            var reason = ex is TaskCanceledException
                ? "timeout or cancellation"
                : ex.GetType().Name;
            return $"Fetch failed: {reason}. {ex.Message}";
        }
    }

    private static string HtmlToText(string html)
    {
        var withoutScripts = ScriptStyleRegex.Replace(html, " ");
        var withBreaks = withoutScripts
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase);
        var withoutTags = TagRegex.Replace(withBreaks, " ");
        return WebUtility.HtmlDecode(withoutTags);
    }
}
