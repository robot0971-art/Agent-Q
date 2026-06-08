namespace AgentQ.Desktop.Services;

public sealed class DesktopAttachment
{
    public required string Path { get; init; }

    public required string FileName { get; init; }

    public required string MediaType { get; init; }

    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public bool IsVideo => MediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

    public bool IsTextDocument => MediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(MediaType, "application/xml", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(MediaType, "application/x-yaml", StringComparison.OrdinalIgnoreCase);
}
