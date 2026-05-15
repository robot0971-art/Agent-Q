using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public static class DesktopPlanParser
{
    private static readonly Regex CheckboxPattern = new(
        @"^\s*[-*]\s+\[(?<mark>[ xX!-])\]\s+(?<title>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex NumberedPattern = new(
        @"^\s*(?<order>\d+)[.)]\s+(?<title>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex BulletPattern = new(
        @"^\s*[-*]\s+(?<title>(?!\[[ xX!-]\]).+)$",
        RegexOptions.Compiled);

    public static IReadOnlyList<AgentPlanItem> Parse(string text)
    {
        var items = new List<AgentPlanItem>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var item = TryParseLine(line, items.Count + 1);
            if (item != null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static AgentPlanItem? TryParseLine(string line, int order)
    {
        var checkbox = CheckboxPattern.Match(line);
        if (checkbox.Success)
        {
            return new AgentPlanItem
            {
                Order = order,
                Status = ParseStatus(checkbox.Groups["mark"].Value),
                Title = CleanTitle(checkbox.Groups["title"].Value)
            };
        }

        var numbered = NumberedPattern.Match(line);
        if (numbered.Success)
        {
            return new AgentPlanItem
            {
                Order = order,
                Title = CleanTitle(numbered.Groups["title"].Value)
            };
        }

        var bullet = BulletPattern.Match(line);
        if (bullet.Success)
        {
            return new AgentPlanItem
            {
                Order = order,
                Title = CleanTitle(bullet.Groups["title"].Value)
            };
        }

        return null;
    }

    private static AgentPlanItemStatus ParseStatus(string mark)
    {
        return mark switch
        {
            "x" or "X" => AgentPlanItemStatus.Done,
            "!" => AgentPlanItemStatus.Blocked,
            "-" => AgentPlanItemStatus.InProgress,
            _ => AgentPlanItemStatus.Pending
        };
    }

    private static string CleanTitle(string title)
    {
        return title.Trim().TrimEnd('.');
    }
}
