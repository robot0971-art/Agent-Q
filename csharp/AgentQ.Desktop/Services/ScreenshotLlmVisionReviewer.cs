using System.IO;
using System.Text;
using System.Text.Json;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public sealed class ScreenshotLlmVisionReviewer(ILlmProvider provider)
{
    public async Task<ScreenshotLlmVisionReviewResult> ReviewAsync(
        ScreenshotLlmVisionReviewRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.Candidate.FullPath))
        {
            return new ScreenshotLlmVisionReviewResult
            {
                RelativePath = request.Candidate.RelativePath,
                Status = ScreenshotLlmVisionReviewStatus.Unknown,
                Summary = "Screenshot file was not found before LLM vision review."
            };
        }

        var mediaType = GetMediaType(request.Candidate.FullPath);
        if (mediaType == null)
        {
            return new ScreenshotLlmVisionReviewResult
            {
                RelativePath = request.Candidate.RelativePath,
                Status = ScreenshotLlmVisionReviewStatus.Unknown,
                Summary = "Screenshot type is not supported for LLM vision review."
            };
        }

        var imageBytes = await File.ReadAllBytesAsync(request.Candidate.FullPath, ct);
        var response = await provider.GenerateResponseAsync(
            CreateContext(request, mediaType, Convert.ToBase64String(imageBytes)),
            [],
            ct);

        var rawResponse = ExtractText(response);
        return ParseResponse(request.Candidate.RelativePath, rawResponse);
    }

    private static ChatContext CreateContext(
        ScreenshotLlmVisionReviewRequest request,
        string mediaType,
        string base64Image)
    {
        return new ChatContext
        {
            Model = string.Empty,
            Stream = false,
            MaxTokens = 700,
            SystemPrompt = "You are a strict UI verification reviewer. Inspect the screenshot and decide whether the rendered UI is acceptable. Focus on blank screens, broken layout, clipped or overlapping text, missing primary controls, and obvious visual regressions. Return only JSON.",
            Messages =
            [
                new ChatMessage
                {
                    Role = ChatRole.User,
                    Content =
                    [
                        ChatContent.CreateText(BuildPrompt(request)),
                        ChatContent.CreateImage(mediaType, base64Image)
                    ]
                }
            ]
        };
    }

    private static string BuildPrompt(ScreenshotLlmVisionReviewRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Review this Playwright screenshot for UI regressions.");
        builder.AppendLine($"Screenshot: {request.Candidate.RelativePath}");
        if (!string.IsNullOrWhiteSpace(request.Candidate.Reason))
        {
            builder.AppendLine($"Review reason: {request.Candidate.Reason}");
        }

        if (request.HeuristicResult != null)
        {
            builder.AppendLine($"Heuristic status: {request.HeuristicResult.Status}");
            builder.AppendLine($"Heuristic message: {request.HeuristicResult.Message}");
            builder.AppendLine($"Brightness: {request.HeuristicResult.AverageBrightness:0.000}");
            builder.AppendLine($"Variance: {request.HeuristicResult.BrightnessVariance:0.0000}");
        }

        if (request.Evidence.Count > 0)
        {
            builder.AppendLine("Verification evidence:");
            foreach (var item in request.Evidence.Take(8))
            {
                builder.AppendLine($"- {item}");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.VerificationOutput))
        {
            builder.AppendLine("Verification output:");
            builder.AppendLine(TrimForPrompt(request.VerificationOutput, 2000));
        }

        builder.AppendLine("Return JSON with shape: {\"status\":\"pass|warning|fail\",\"summary\":\"short sentence\",\"findings\":[\"finding\"]}");
        return builder.ToString();
    }

    private static ScreenshotLlmVisionReviewResult ParseResponse(string relativePath, string rawResponse)
    {
        var json = ExtractJsonObject(rawResponse);
        if (json != null)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                return new ScreenshotLlmVisionReviewResult
                {
                    RelativePath = relativePath,
                    Status = ParseStatus(ReadString(root, "status")),
                    Summary = ReadString(root, "summary") ?? "LLM vision review completed.",
                    Findings = ReadStringArray(root, "findings"),
                    RawResponse = rawResponse
                };
            }
            catch (JsonException)
            {
                // Fall through to a conservative text response below.
            }
        }

        return new ScreenshotLlmVisionReviewResult
        {
            RelativePath = relativePath,
            Status = ScreenshotLlmVisionReviewStatus.Unknown,
            Summary = string.IsNullOrWhiteSpace(rawResponse)
                ? "LLM vision review returned no text."
                : TrimForPrompt(rawResponse.Trim(), 500),
            RawResponse = rawResponse
        };
    }

    private static string ExtractText(ChatResponse response)
    {
        return string.Join(
            Environment.NewLine,
            response.Content
                .Where(content => content.Type == ContentType.Text && !string.IsNullOrWhiteSpace(content.Text))
                .Select(content => content.Text!.Trim()));
    }

    private static string? ExtractJsonObject(string value)
    {
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        return start >= 0 && end > start
            ? value[start..(end + 1)]
            : null;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();
    }

    private static ScreenshotLlmVisionReviewStatus ParseStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "pass" or "passed" => ScreenshotLlmVisionReviewStatus.Pass,
            "warning" or "warn" => ScreenshotLlmVisionReviewStatus.Warning,
            "fail" or "failed" => ScreenshotLlmVisionReviewStatus.Fail,
            _ => ScreenshotLlmVisionReviewStatus.Unknown
        };
    }

    private static string? GetMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };
    }

    private static string TrimForPrompt(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }
}
