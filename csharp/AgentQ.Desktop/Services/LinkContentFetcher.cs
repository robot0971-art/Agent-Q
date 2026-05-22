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

    public static bool ContainsUrl(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && UrlRegex.IsMatch(text);
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

        var results = await FetchAsync(text, ct);
        foreach (var result in results)
        {
            builder.AppendLine();
            builder.AppendLine($"URL: {result.Url}");
            builder.AppendLine(FormatResult(result));
        }

        return builder.ToString().Trim();
    }

    public async Task<IReadOnlyList<LinkFetchResult>> FetchAsync(string text, CancellationToken ct)
    {
        var urls = UrlRegex.Matches(text)
            .Select(match => match.Value.TrimEnd('.', ',', ')', ']', '}'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        var results = new List<LinkFetchResult>();
        foreach (var url in urls)
        {
            results.Add(await FetchAsync(new Uri(url), ct));
        }

        return results;
    }

    private async Task<LinkFetchResult> FetchAsync(Uri uri, CancellationToken ct)
    {
        var url = uri.ToString();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("AgentQ-Desktop/1.0");
            request.Headers.Accept.ParseAdd("text/html, text/plain;q=0.9, */*;q=0.1");

            var httpClient = _httpClientFactory.CreateClient("desktop-links");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!response.IsSuccessStatusCode)
            {
                return new LinkFetchResult
                {
                    Url = url,
                    Status = LinkFetchStatus.HttpError,
                    HttpStatusCode = (int)response.StatusCode,
                    HttpReasonPhrase = response.ReasonPhrase,
                    ContentType = mediaType,
                    FailureReason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                };
            }

            if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Contains("text", StringComparison.OrdinalIgnoreCase))
            {
                return new LinkFetchResult
                {
                    Url = url,
                    Status = LinkFetchStatus.UnsupportedContentType,
                    HttpStatusCode = (int)response.StatusCode,
                    HttpReasonPhrase = response.ReasonPhrase,
                    ContentType = mediaType,
                    FailureReason = string.IsNullOrWhiteSpace(mediaType)
                        ? "unsupported or missing content type"
                        : $"unsupported content type {mediaType}"
                };
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

            if (string.IsNullOrWhiteSpace(plainText))
            {
                return new LinkFetchResult
                {
                    Url = url,
                    Status = LinkFetchStatus.EmptyContent,
                    HttpStatusCode = (int)response.StatusCode,
                    HttpReasonPhrase = response.ReasonPhrase,
                    ContentType = mediaType,
                    FailureReason = "no readable text found"
                };
            }

            return new LinkFetchResult
            {
                Url = url,
                Status = LinkFetchStatus.Succeeded,
                HttpStatusCode = (int)response.StatusCode,
                HttpReasonPhrase = response.ReasonPhrase,
                ContentType = mediaType,
                Excerpt = plainText
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new LinkFetchResult
            {
                Url = url,
                Status = ex switch
                {
                    TaskCanceledException => LinkFetchStatus.TimeoutOrCancellation,
                    UriFormatException => LinkFetchStatus.InvalidUrl,
                    _ => LinkFetchStatus.RequestFailed
                },
                FailureReason = ex is TaskCanceledException
                    ? $"timeout or cancellation. {ex.Message}"
                    : $"{ex.GetType().Name}. {ex.Message}"
            };
        }
    }

    private static string FormatResult(LinkFetchResult result)
    {
        if (result.Succeeded)
        {
            return $"Fetch succeeded. Status: HTTP {result.HttpStatusCode}. Content type: {result.ContentType}. Readable text excerpt: {result.Excerpt}";
        }

        return result.Status switch
        {
            LinkFetchStatus.HttpError => $"Fetch failed: HTTP {result.HttpStatusCode} {result.HttpReasonPhrase}. Failure reason: {result.FailureReason}.",
            LinkFetchStatus.UnsupportedContentType => $"Fetch failed: unsupported content type. Content type: {result.ContentType}. Failure reason: {result.FailureReason}.",
            LinkFetchStatus.EmptyContent => $"Fetch failed: no readable text found. Failure reason: {result.FailureReason}.",
            LinkFetchStatus.TimeoutOrCancellation => $"Fetch failed: timeout or cancellation. Failure reason: {result.FailureReason}.",
            LinkFetchStatus.InvalidUrl => $"Fetch failed: invalid URL. Failure reason: {result.FailureReason}.",
            _ => $"Fetch failed: request failed. Failure reason: {result.FailureReason}."
        };
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
