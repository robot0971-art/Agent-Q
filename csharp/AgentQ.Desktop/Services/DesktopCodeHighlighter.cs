using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace AgentQ.Desktop.Services;

public static class DesktopCodeHighlighter
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "case", "catch", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "record", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual",
        "void", "volatile", "while"
    };

    private static readonly MediaBrush DefaultBrush = Brush("#D4D4D4");
    private static readonly MediaBrush KeywordBrush = Brush("#569CD6");
    private static readonly MediaBrush TypeBrush = Brush("#4EC9B0");
    private static readonly MediaBrush StringBrush = Brush("#CE9178");
    private static readonly MediaBrush CommentBrush = Brush("#6A9955");
    private static readonly MediaBrush NumberBrush = Brush("#B5CEA8");
    private static readonly MediaBrush AttributeBrush = Brush("#C586C0");

    public static FlowDocument CreateDocument(string source)
    {
        var document = new FlowDocument
        {
            Background = Brush("#1E1E1E"),
            Foreground = DefaultBrush,
            FontFamily = new MediaFontFamily("Consolas"),
            FontSize = 13,
            PagePadding = new Thickness(10),
            LineHeight = 18,
            PageWidth = 5000,
            ColumnWidth = 5000
        };

        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                LineHeight = 18
            };
            paragraph.Inlines.Add(new Run($"{index + 1,4}  ")
            {
                Foreground = Brush("#858585")
            });
            AddHighlightedLine(paragraph, lines[index]);
            document.Blocks.Add(paragraph);
        }

        return document;
    }

    private static void AddHighlightedLine(Paragraph paragraph, string line)
    {
        var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
        var code = commentIndex >= 0 ? line[..commentIndex] : line;
        var comment = commentIndex >= 0 ? line[commentIndex..] : string.Empty;

        var position = 0;
        while (position < code.Length)
        {
            var character = code[position];
            if (character == '"')
            {
                var end = FindStringEnd(code, position);
                paragraph.Inlines.Add(Run(code[position..end], StringBrush));
                position = end;
                continue;
            }

            if (character == '[')
            {
                var end = code.IndexOf(']', position);
                if (end >= 0)
                {
                    paragraph.Inlines.Add(Run(code[position..(end + 1)], AttributeBrush));
                    position = end + 1;
                    continue;
                }
            }

            if (char.IsLetter(character) || character == '_')
            {
                var end = position + 1;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_'))
                {
                    end++;
                }

                var token = code[position..end];
                var brush = CSharpKeywords.Contains(token)
                    ? KeywordBrush
                    : char.IsUpper(token[0]) ? TypeBrush : DefaultBrush;
                paragraph.Inlines.Add(Run(token, brush));
                position = end;
                continue;
            }

            if (char.IsDigit(character))
            {
                var end = position + 1;
                while (end < code.Length && (char.IsDigit(code[end]) || code[end] == '.'))
                {
                    end++;
                }

                paragraph.Inlines.Add(Run(code[position..end], NumberBrush));
                position = end;
                continue;
            }

            paragraph.Inlines.Add(Run(character.ToString(), DefaultBrush));
            position++;
        }

        if (!string.IsNullOrEmpty(comment))
        {
            paragraph.Inlines.Add(Run(comment, CommentBrush));
        }
    }

    private static int FindStringEnd(string text, int start)
    {
        var position = start + 1;
        while (position < text.Length)
        {
            if (text[position] == '"' && text[position - 1] != '\\')
            {
                return position + 1;
            }

            position++;
        }

        return text.Length;
    }

    private static Run Run(string text, MediaBrush brush) => new(text)
    {
        Foreground = brush
    };

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
