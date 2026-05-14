using System.Diagnostics;
using System.IO;

namespace AgentQ.Desktop.Services;

public sealed record VideoFrameExtractionResult(bool IsAvailable, IReadOnlyList<string> FramePaths, string? Error);

public static class VideoFrameExtractor
{
    private const int MaxFrames = 4;

    public static async Task<VideoFrameExtractionResult> ExtractFramesAsync(string videoPath, CancellationToken ct)
    {
        if (!IsFfmpegAvailable())
        {
            return new VideoFrameExtractionResult(false, [], "ffmpeg not found");
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), "AgentQ", "VideoFrames", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var outputPattern = Path.Combine(outputDirectory, "frame-%03d.jpg");
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add("fps=1/3,scale=768:-2:force_original_aspect_ratio=decrease");
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add(MaxFrames.ToString());
        startInfo.ArgumentList.Add(outputPattern);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return new VideoFrameExtractionResult(true, [], "failed to start ffmpeg");
        }

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            TryDeleteDirectory(outputDirectory);
            return new VideoFrameExtractionResult(true, [], stderr);
        }

        var frames = Directory.GetFiles(outputDirectory, "frame-*.jpg")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFrames)
            .ToList();

        return new VideoFrameExtractionResult(true, frames, null);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temporary frame cleanup is best-effort.
        }
    }

    private static bool IsFfmpegAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-version");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            if (!process.WaitForExit(1500))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
