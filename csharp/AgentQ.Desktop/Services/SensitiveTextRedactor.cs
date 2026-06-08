using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public static class SensitiveTextRedactor
{
    private const string Mask = "[REDACTED]";
    private static readonly Regex AuthorizationBearerPattern = new(@"(?<prefix>\bAuthorization\s*[:=]\s*Bearer\s+)[^\s""'}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex KeyValueSecretPattern = new(@"(?<prefix>\b(?:api[_-]?key|x-api-key|token|access[_-]?token|refresh[_-]?token|secret|password|passwd|pwd)\s*=\s*)[^\s;&""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex JsonSecretPattern = new(@"(?<prefix>[""'](?:api[_-]?key|apikey|x-api-key|token|access[_-]?token|refresh[_-]?token|secret|password|passwd|pwd)[""']\s*:\s*(?<quote>[""']))[^""']+(?<quote2>[""'])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UrlQuerySecretPattern = new(@"(?<prefix>[?&](?:api[_-]?key|apikey|token|access[_-]?token|refresh[_-]?token|secret|password|passwd|pwd)=)[^&#\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UserInfoUrlPattern = new(@"(?<scheme>\b[a-z][a-z0-9+.-]*://)[^/\s:@]+:[^@\s/]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OpenAiLikeTokenPattern = new(@"(?<prefix>\b(?:sk|pk|rk|ghp|github_pat|glpat)-)[A-Za-z0-9_\-]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value) || !MayContainSecret(value))
        {
            return value;
        }

        var text = AuthorizationBearerPattern.Replace(value, "${prefix}" + Mask);
        text = KeyValueSecretPattern.Replace(text, match => match.Groups["prefix"].Value + Mask);
        text = JsonSecretPattern.Replace(text, match => match.Groups["prefix"].Value + Mask + match.Groups["quote"].Value);
        text = UrlQuerySecretPattern.Replace(text, match => match.Groups["prefix"].Value + Mask);
        text = UserInfoUrlPattern.Replace(text, match => match.Groups["scheme"].Value + Mask + "@");
        text = OpenAiLikeTokenPattern.Replace(text, match => match.Groups["prefix"].Value + Mask);
        return text;
    }

    private static bool MayContainSecret(string value) =>
        value.Contains("key", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("://", StringComparison.Ordinal);

}
