using System.IO;

namespace AgentQ.Desktop.Services;

public static class VisualEvidenceService
{
    public static IReadOnlyList<VisualEvidenceEntry> InspectAttachments(IEnumerable<DesktopAttachment> attachments)
    {
        return attachments.Select(InspectAttachment).ToList();
    }

    public static IReadOnlyList<string> BuildPromptNotes(IEnumerable<DesktopAttachment> attachments)
    {
        return InspectAttachments(attachments)
            .Select(entry =>
            {
                var dimensions = entry.Width > 0 && entry.Height > 0
                    ? $", {entry.Width:0}x{entry.Height:0}"
                    : string.Empty;
                return $"Visual evidence attached: {entry.Kind} {entry.FileName} ({entry.MediaType}{dimensions}, {entry.SizeKb:0} KB).";
            })
            .ToList();
    }

    public static string BuildTimelineDetail(VisualEvidenceEntry entry)
    {
        var dimensions = entry.Width > 0 && entry.Height > 0
            ? $", dimensions {entry.Width:0}x{entry.Height:0}"
            : string.Empty;
        return $"{entry.Kind}: {entry.FileName}, {entry.MediaType}, {entry.SizeKb:0} KB{dimensions}. Path: {entry.Path}";
    }

    private static VisualEvidenceEntry InspectAttachment(DesktopAttachment attachment)
    {
        var sizeKb = TryGetSizeKb(attachment.Path);
        var (width, height) = attachment.IsImage
            ? TryReadImageDimensions(attachment.Path)
            : (0, 0);

        return new VisualEvidenceEntry
        {
            Path = attachment.Path,
            FileName = attachment.FileName,
            MediaType = attachment.MediaType,
            Kind = attachment.IsImage ? "image" : attachment.IsVideo ? "video" : "attachment",
            SizeKb = sizeKb,
            Width = width,
            Height = height
        };
    }

    private static int TryGetSizeKb(string path)
    {
        try
        {
            var bytes = new FileInfo(path).Length;
            return Math.Max(1, (int)Math.Ceiling(bytes / 1024d));
        }
        catch
        {
            return 0;
        }
    }

    private static (int Width, int Height) TryReadImageDimensions(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[32];
            var read = stream.Read(header);
            if (read < 10)
            {
                return (0, 0);
            }

            if (IsPng(header))
            {
                return (ReadBigEndianInt32(header[16..20]), ReadBigEndianInt32(header[20..24]));
            }

            if (IsBmp(header))
            {
                return (ReadLittleEndianInt32(header[18..22]), Math.Abs(ReadLittleEndianInt32(header[22..26])));
            }
        }
        catch
        {
            return (0, 0);
        }

        return TryReadJpegDimensions(path);
    }

    private static (int Width, int Height) TryReadJpegDimensions(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
            {
                return (0, 0);
            }

            while (stream.Position < stream.Length)
            {
                if (stream.ReadByte() != 0xFF)
                {
                    continue;
                }

                var marker = stream.ReadByte();
                while (marker == 0xFF)
                {
                    marker = stream.ReadByte();
                }

                if (marker is < 0 or 0xD9 or 0xDA)
                {
                    break;
                }

                var length = ReadBigEndianUInt16(stream);
                if (length < 2)
                {
                    break;
                }

                if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
                {
                    stream.ReadByte();
                    var height = ReadBigEndianUInt16(stream);
                    var width = ReadBigEndianUInt16(stream);
                    return (width, height);
                }

                stream.Seek(length - 2, SeekOrigin.Current);
            }
        }
        catch
        {
            return (0, 0);
        }

        return (0, 0);
    }

    private static bool IsPng(ReadOnlySpan<byte> header) =>
        header.Length >= 24 &&
        header[0] == 0x89 &&
        header[1] == 0x50 &&
        header[2] == 0x4E &&
        header[3] == 0x47;

    private static bool IsBmp(ReadOnlySpan<byte> header) =>
        header.Length >= 26 &&
        header[0] == 0x42 &&
        header[1] == 0x4D;

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes) =>
        bytes.Length < 4 ? 0 : (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private static int ReadLittleEndianInt32(ReadOnlySpan<byte> bytes) =>
        bytes.Length < 4 ? 0 : bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24);

    private static int ReadBigEndianUInt16(Stream stream)
    {
        var high = stream.ReadByte();
        var low = stream.ReadByte();
        return high < 0 || low < 0 ? 0 : (high << 8) | low;
    }
}

public sealed class VisualEvidenceEntry
{
    public string Path { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public int SizeKb { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}
