using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public static partial class FailureFingerprintService
{
    public static string Create(string title, string? detail)
    {
        var lines = ExtractFailureLines(detail ?? string.Empty);
        var seed = Normalize($"{title}\n{string.Join('\n', lines)}");
        if (string.IsNullOrWhiteSpace(seed))
        {
            seed = Normalize(title);
        }

        if (string.IsNullOrWhiteSpace(seed))
        {
            return string.Empty;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        return $"failure-{hash[..16]}";
    }

    private static IReadOnlyList<string> ExtractFailureLines(string detail)
    {
        var lines = detail.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Where(line => FailureLineRegex().IsMatch(line))
            .Take(5)
            .ToList();

        return lines.Count > 0
            ? lines
            : detail.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Take(3)
                .ToList();
    }

    private static string Normalize(string value)
    {
        var normalized = value.ToLowerInvariant();
        normalized = WindowsPathRegex().Replace(normalized, "<path>");
        normalized = UnixPathRegex().Replace(normalized, "<path>");
        normalized = LineColumnRegex().Replace(normalized, ":<line>");
        normalized = QuotedTempPathRegex().Replace(normalized, "\"<path>\"");
        normalized = HexRegex().Replace(normalized, "0x<hex>");
        normalized = WhitespaceRegex().Replace(normalized, " ");
        return normalized.Trim();
    }

    [GeneratedRegex("""(error\s+[a-z]{1,5}\d{3,5}|failed|failure|exception|assert\.|timeout|timed out|denied|not found|command not found|status\s+\d{3}|http\s+\d{3})""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FailureLineRegex();

    [GeneratedRegex("""[a-z]:\\[^\s:"']+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex("""(?<!https?:)/[^\s:"']+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnixPathRegex();

    [GeneratedRegex(""":\d+(?::\d+)?""", RegexOptions.CultureInvariant)]
    private static partial Regex LineColumnRegex();

    [GeneratedRegex("""["'][^"']*(?:temp|tmp|agentq-tests|bin|obj)[^"']*["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedTempPathRegex();

    [GeneratedRegex("""0x[0-9a-f]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HexRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
