using System.Text;

namespace AgentQ.Tools;

internal sealed record TextFileContent(string Content, Encoding Encoding);

internal static class TextFileIo
{
    private const int BinarySampleSize = 4096;

    public static readonly Encoding DefaultEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static TextFileContent ReadAllTextPreservingEncoding(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (LooksBinary(stream))
        {
            throw new InvalidOperationException("File appears to be binary; refusing text edit.");
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream, DefaultEncoding, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        return new TextFileContent(content, reader.CurrentEncoding);
    }

    public static void WriteAllTextAtomically(string path, string content, Encoding? encoding = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        directory ??= Environment.CurrentDirectory;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var resolvedTempPath = Path.GetFullPath(tempPath);

        try
        {
            File.WriteAllText(resolvedTempPath, content, encoding ?? DefaultEncoding);

            if (File.Exists(path))
            {
                File.Replace(resolvedTempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(resolvedTempPath, path);
            }
        }
        finally
        {
            if (File.Exists(resolvedTempPath))
            {
                File.Delete(resolvedTempPath);
            }
        }
    }

    private static bool LooksBinary(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[BinarySampleSize];
        var bytesRead = stream.Read(buffer);
        if (HasTextEncodingPreamble(buffer[..bytesRead]))
        {
            return false;
        }

        return buffer[..bytesRead].Contains((byte)0);
    }

    private static bool HasTextEncodingPreamble(ReadOnlySpan<byte> bytes)
    {
        return bytes.StartsWith(Encoding.UTF8.GetPreamble()) ||
               bytes.StartsWith(Encoding.Unicode.GetPreamble()) ||
               bytes.StartsWith(Encoding.BigEndianUnicode.GetPreamble()) ||
               bytes.StartsWith(Encoding.UTF32.GetPreamble());
    }
}
