namespace AgentQ.Desktop.Services;

public static class LineDiffBuilder
{
    private const int MaxDiffInputLines = 1200;

    public static IReadOnlyList<DiffLine> Build(string before, string after)
    {
        var beforeLines = SplitLines(before);
        var afterLines = SplitLines(after);

        if (beforeLines.Length > MaxDiffInputLines || afterLines.Length > MaxDiffInputLines)
        {
            return BuildCompactDiff(beforeLines, afterLines);
        }

        var table = new int[beforeLines.Length + 1, afterLines.Length + 1];
        for (var i = beforeLines.Length - 1; i >= 0; i--)
        {
            for (var j = afterLines.Length - 1; j >= 0; j--)
            {
                table[i, j] = beforeLines[i] == afterLines[j]
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var result = new List<DiffLine>();
        var left = 0;
        var right = 0;

        while (left < beforeLines.Length && right < afterLines.Length)
        {
            if (beforeLines[left] == afterLines[right])
            {
                result.Add(new DiffLine { Kind = DiffLineKind.Unchanged, Text = beforeLines[left] });
                left++;
                right++;
            }
            else if (table[left + 1, right] >= table[left, right + 1])
            {
                result.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = beforeLines[left] });
                left++;
            }
            else
            {
                result.Add(new DiffLine { Kind = DiffLineKind.Added, Text = afterLines[right] });
                right++;
            }
        }

        while (left < beforeLines.Length)
        {
            result.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = beforeLines[left++] });
        }

        while (right < afterLines.Length)
        {
            result.Add(new DiffLine { Kind = DiffLineKind.Added, Text = afterLines[right++] });
        }

        return result;
    }

    private static IReadOnlyList<DiffLine> BuildCompactDiff(string[] beforeLines, string[] afterLines)
    {
        var result = new List<DiffLine>
        {
            new() { Kind = DiffLineKind.Removed, Text = $"[large file before: {beforeLines.Length} lines]" },
            new() { Kind = DiffLineKind.Added, Text = $"[large file after: {afterLines.Length} lines]" }
        };
        return result;
    }

    private static string[] SplitLines(string text)
    {
        return text.ReplaceLineEndings("\n").Split('\n');
    }
}
