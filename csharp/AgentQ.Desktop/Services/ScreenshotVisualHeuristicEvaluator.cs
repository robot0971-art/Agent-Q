using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AgentQ.Desktop.Services;

public sealed class ScreenshotVisualHeuristicEvaluator
{
    private const double DarkScreenBrightnessThreshold = 0.04;
    private const double BlankVarianceThreshold = 0.0008;
    private const int MinimumPixelsForReview = 100;

    public ScreenshotVisualReviewResult Evaluate(ScreenshotVisualReviewCandidate candidate)
    {
        try
        {
            return EvaluateDecoded(candidate);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return new ScreenshotVisualReviewResult
            {
                RelativePath = candidate.RelativePath,
                Status = ScreenshotVisualReviewStatus.Warning,
                Message = $"Could not decode screenshot for heuristic review: {ex.Message}"
            };
        }
    }

    private static ScreenshotVisualReviewResult EvaluateDecoded(ScreenshotVisualReviewCandidate candidate)
    {
        using var stream = File.OpenRead(candidate.FullPath);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var pixelCount = width * height;
        if (pixelCount < MinimumPixelsForReview)
        {
            return new ScreenshotVisualReviewResult
            {
                RelativePath = candidate.RelativePath,
                Status = ScreenshotVisualReviewStatus.Warning,
                Message = "Screenshot dimensions are too small for reliable visual review.",
                SampledPixels = pixelCount
            };
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var sampleStep = Math.Max(1, pixelCount / 5000);
        var sampled = 0;
        var sum = 0d;
        var sumSquares = 0d;
        for (var index = 0; index < pixelCount; index += sampleStep)
        {
            var offset = index * 4;
            var b = pixels[offset] / 255d;
            var g = pixels[offset + 1] / 255d;
            var r = pixels[offset + 2] / 255d;
            var brightness = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
            sum += brightness;
            sumSquares += brightness * brightness;
            sampled++;
        }

        var average = sum / sampled;
        var variance = Math.Max(0, (sumSquares / sampled) - (average * average));
        return Classify(candidate.RelativePath, average, variance, sampled);
    }

    private static ScreenshotVisualReviewResult Classify(
        string relativePath,
        double average,
        double variance,
        int sampled)
    {
        if (average <= DarkScreenBrightnessThreshold)
        {
            return Create(relativePath, ScreenshotVisualReviewStatus.Fail, average, variance, sampled, "Screenshot appears almost entirely dark or blank.");
        }

        if (variance <= BlankVarianceThreshold)
        {
            return Create(relativePath, ScreenshotVisualReviewStatus.Warning, average, variance, sampled, "Screenshot has very low visual variance and may be blank, solid, or stuck on a loading state.");
        }

        return Create(relativePath, ScreenshotVisualReviewStatus.Pass, average, variance, sampled, "Screenshot passed first-pass heuristic review.");
    }

    private static ScreenshotVisualReviewResult Create(
        string relativePath,
        ScreenshotVisualReviewStatus status,
        double average,
        double variance,
        int sampled,
        string message)
    {
        return new ScreenshotVisualReviewResult
        {
            RelativePath = relativePath,
            Status = status,
            AverageBrightness = average,
            BrightnessVariance = variance,
            SampledPixels = sampled,
            Message = message
        };
    }
}
