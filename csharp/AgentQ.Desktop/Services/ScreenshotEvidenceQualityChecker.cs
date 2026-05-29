using System.IO;
using System.Security.Cryptography;

namespace AgentQ.Desktop.Services;

public sealed class ScreenshotEvidenceQualityChecker
{
    private const long MinimumUsefulScreenshotBytes = 512;

    private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public IReadOnlyList<ScreenshotEvidenceQuality> Check(
        IReadOnlyList<VerificationArtifact> artifacts,
        string workspaceRoot)
    {
        if (artifacts.Count == 0 || !Directory.Exists(workspaceRoot))
        {
            return [];
        }

        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return artifacts
            .Where(artifact => artifact.Kind.Equals("screenshot", StringComparison.OrdinalIgnoreCase))
            .Select(artifact => CheckOne(artifact, workspaceRoot, seenHashes))
            .ToList();
    }

    private static ScreenshotEvidenceQuality CheckOne(
        VerificationArtifact artifact,
        string workspaceRoot,
        HashSet<string> seenHashes)
    {
        var extension = Path.GetExtension(artifact.Path);
        if (!SupportedExtensions.Any(item => item.Equals(extension, StringComparison.OrdinalIgnoreCase)))
        {
            return Create(artifact.Path, ScreenshotEvidenceQualityStatus.UnsupportedExtension, 0, "Unsupported screenshot extension.");
        }

        var fullPath = ResolvePath(workspaceRoot, artifact.Path);
        if (fullPath == null || !File.Exists(fullPath))
        {
            return Create(artifact.Path, ScreenshotEvidenceQualityStatus.Missing, 0, "Screenshot file is missing.");
        }

        var info = new FileInfo(fullPath);
        if (info.Length == 0)
        {
            return Create(artifact.Path, ScreenshotEvidenceQualityStatus.Empty, 0, "Screenshot file is empty.");
        }

        if (info.Length < MinimumUsefulScreenshotBytes)
        {
            return Create(artifact.Path, ScreenshotEvidenceQualityStatus.TooSmall, info.Length, "Screenshot is unusually small and may not be useful.");
        }

        var hash = HashFile(fullPath);
        if (!seenHashes.Add(hash))
        {
            return Create(artifact.Path, ScreenshotEvidenceQualityStatus.Duplicate, info.Length, "Screenshot duplicates an earlier artifact.");
        }

        return Create(artifact.Path, ScreenshotEvidenceQualityStatus.Valid, info.Length, "Screenshot is present and ready for visual review.");
    }

    private static string? ResolvePath(string workspaceRoot, string artifactPath)
    {
        if (Path.IsPathRooted(artifactPath))
        {
            return null;
        }

        var root = Path.GetFullPath(workspaceRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, artifactPath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static ScreenshotEvidenceQuality Create(
        string path,
        ScreenshotEvidenceQualityStatus status,
        long sizeBytes,
        string message)
    {
        return new ScreenshotEvidenceQuality
        {
            Path = path,
            Status = status,
            SizeBytes = sizeBytes,
            Message = message
        };
    }
}
