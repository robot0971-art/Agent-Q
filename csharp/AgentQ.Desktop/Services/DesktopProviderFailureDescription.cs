using System.Net;
using System.Net.Http;

namespace AgentQ.Desktop.Services;

public sealed record DesktopProviderFailureDescription(
    string Title,
    string StatusText,
    string Detail,
    string LogText);

public static class DesktopProviderFailureClassifier
{
    public static DesktopProviderFailureDescription Describe(Exception ex)
    {
        if (ex is HttpRequestException http)
        {
            return http.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new DesktopProviderFailureDescription(
                    "Provider authentication failed",
                    $"Error: provider authentication failed. Check the selected provider, API key, and base URL. {http.Message}",
                    $"Provider authentication failed: {http.Message}",
                    $"Provider auth error: {http.Message}"),
                HttpStatusCode.TooManyRequests => new DesktopProviderFailureDescription(
                    "Provider rate limit reached",
                    $"Error: provider rate limit reached. Wait briefly or switch model/provider. {http.Message}",
                    $"Provider rate limit reached: {http.Message}",
                    $"Provider rate limit: {http.Message}"),
                _ when http.StatusCode != null && (int)http.StatusCode >= 500 => new DesktopProviderFailureDescription(
                    "Provider service error",
                    $"Error: provider service error. AgentQ retried retryable failures before surfacing this. {http.Message}",
                    $"Provider service error: {http.Message}",
                    $"Provider service error: {http.Message}"),
                _ => new DesktopProviderFailureDescription(
                    "Provider request failed",
                    $"Error: provider request failed. {http.Message}",
                    $"Provider request failed: {http.Message}",
                    $"Provider request failed: {http.Message}")
            };
        }

        if (LooksLikeOutputLengthError(ex.Message))
        {
            return new DesktopProviderFailureDescription(
                "Provider output length exceeded",
                $"Error: provider output length exceeded. Ask AgentQ to continue in smaller chunks or reduce attached/log context. {ex.Message}",
                $"Provider output length exceeded: {ex.Message}",
                $"Provider output length exceeded: {ex.Message}");
        }

        return new DesktopProviderFailureDescription(
            "Run failed",
            $"Error: {ex.Message}",
            ex.Message,
            $"Error: {ex.Message}");
    }

    private static bool LooksLikeOutputLengthError(string message) =>
        message.Contains("output length", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("maximum context", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("max tokens", StringComparison.OrdinalIgnoreCase);
}
