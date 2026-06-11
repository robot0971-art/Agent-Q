using System.IO;
using System.Text;

namespace AgentQ.Desktop.Services;

public sealed class SystemSkillService
{
    public const int DefaultMaxSkills = 3;
    public const int DefaultMaxSkillContentChars = 4000;

    private readonly int _maxSkills;
    private readonly int _maxSkillContentChars;

    public SystemSkillService()
        : this(DefaultMaxSkills, DefaultMaxSkillContentChars)
    {
    }

    public SystemSkillService(int maxSkills, int maxSkillContentChars)
    {
        _maxSkills = Math.Max(1, maxSkills);
        _maxSkillContentChars = Math.Max(200, maxSkillContentChars);
    }

    public IReadOnlyList<AgentQSystemSkill> SelectRelevantSkills(
        string userText,
        string workspaceRoot,
        DesktopTaskProfile taskProfile,
        ProjectAgentConfig? projectConfig = null)
    {
        var normalizedUserText = Normalize(userText);
        if (string.IsNullOrWhiteSpace(normalizedUserText))
        {
            return [];
        }

        var skills = LoadSkills(workspaceRoot);
        return skills
            .Where(skill => IsRelevant(skill, normalizedUserText, taskProfile))
            .OrderByDescending(skill => skill.Priority)
            .ThenByDescending(skill => string.Equals(skill.Source, AgentQSystemSkillSource.Project, StringComparison.OrdinalIgnoreCase))
            .ThenBy(skill => skill.Id, StringComparer.OrdinalIgnoreCase)
            .Take(_maxSkills)
            .ToList();
    }

    public string BuildContext(IReadOnlyList<AgentQSystemSkill> skills)
    {
        if (skills.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Relevant AgentQ system skills:");

        foreach (var skill in skills)
        {
            builder.AppendLine();
            builder.AppendLine($"[{skill.Id}] {skill.Title}");
            builder.AppendLine(Truncate(skill.Content));
        }

        return builder.ToString().TrimEnd();
    }

    public static bool RequiresToolUseForFileProducingTask(
        IReadOnlyList<AgentQSystemSkill> skills,
        string userText,
        DesktopTaskProfile taskProfile)
    {
        if (taskProfile.Kind != DesktopTaskKind.Feature ||
            !skills.Any(skill => string.Equals(skill.Id, "greenfield-project-scaffold", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var normalized = Normalize(userText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (ContainsAnyNormalized(
                normalized,
                "isitpossible",
                "woulditbepossible",
                "canwe",
                "cani",
                "couldwe",
                "couldi",
                "\uAC00\uB2A5\uD55C\uAC00",
                "\uAC00\uB2A5\uD560\uAE4C",
                "\uAC00\uB2A5\uD574",
                "\uAC00\uB2A5\uD560\uAE4C\uC694",
                "\uC218\uC788\uC744\uAE4C",
                "\uD574\uBCFC\uC218\uC788",
                "\uC5B4\uB5A8\uAE4C",
                "\uAD1C\uCC2E\uC744\uAE4C") &&
            !ContainsAnyNormalized(
                normalized,
                "makeitnow",
                "builditnow",
                "createitnow",
                "implementitnow",
                "pleasecreate",
                "pleaseimplement",
                "\uBC14\uB85C\uB9CC\uB4E4",
                "\uBC14\uB85C\uC0DD\uC131",
                "\uBC14\uB85C\uAD6C\uD604",
                "\uC774\uB300\uB85C\uB9CC\uB4E4",
                "\uC774\uB300\uB85C\uC0DD\uC131",
                "\uC774\uB300\uB85C\uAD6C\uD604",
                "\uB9CC\uB4E4\uC5B4\uC918",
                "\uC0DD\uC131\uD574\uC918",
                "\uAD6C\uD604\uD574\uC918",
                "\uC9C4\uD589\uD574"))
        {
            return false;
        }

        return ContainsAnyNormalized(
            normalized,
            "create",
            "make",
            "build",
            "write",
            "generate",
            "scaffold",
            "unreal",
            "playercontroller",
            "c++",
            "project",
            "app",
            "portfolio",
            "homepage",
            "website",
            "landingpage",
            "blog",
            "wordbook",
            "shopping",
            "\uB9CC\uB4E4",
            "\uD648\uD398\uC774\uC9C0",
            "\uC791\uC131",
            "\uC0DD\uC131",
            "\uAD6C\uD604",
            "\uD504\uB85C\uC81D\uD2B8",
            "\uC571",
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624",
            "\uC6F9\uC0AC\uC774\uD2B8",
            "\uB79C\uB529",
            "\uBE14\uB85C\uADF8",
            "\uB2E8\uC5B4\uC7A5",
            "\uC1FC\uD551");
    }

    private IReadOnlyList<AgentQSystemSkill> LoadSkills(string workspaceRoot)
    {
        var merged = new Dictionary<string, AgentQSystemSkill>(StringComparer.OrdinalIgnoreCase);
        var projectSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in LoadBuiltinSkills())
        {
            merged[skill.Id] = skill;
        }

        foreach (var skill in LoadProjectSkills(workspaceRoot))
        {
            if (!projectSkillIds.Add(skill.Id))
            {
                continue;
            }

            merged[skill.Id] = skill;
        }

        return merged.Values.ToList();
    }

    private static IEnumerable<AgentQSystemSkill> LoadBuiltinSkills()
    {
        var assembly = typeof(SystemSkillService).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.Contains(".SystemSkills.", StringComparison.Ordinal) &&
                                    name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            AgentQSystemSkill? skill = null;
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                skill = TryParseSkill(reader.ReadToEnd(), AgentQSystemSkillSource.Builtin);
            }
            catch
            {
                skill = null;
            }

            if (skill is not null)
            {
                yield return skill;
            }
        }
    }

    private static IEnumerable<AgentQSystemSkill> LoadProjectSkills(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            yield break;
        }

        var skillsDirectory = Path.Combine(workspaceRoot, ".agentq", "skills");
        if (!Directory.Exists(skillsDirectory))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(skillsDirectory, "*.md", SearchOption.TopDirectoryOnly).ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            AgentQSystemSkill? skill = null;
            try
            {
                skill = TryParseSkill(File.ReadAllText(file, Encoding.UTF8), AgentQSystemSkillSource.Project);
            }
            catch
            {
                skill = null;
            }

            if (skill is not null)
            {
                yield return skill;
            }
        }
    }

    private static AgentQSystemSkill? TryParseSkill(string markdown, string source)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var normalizedMarkdown = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (!normalizedMarkdown.StartsWith("---\n", StringComparison.Ordinal))
        {
            return null;
        }

        var endIndex = normalizedMarkdown.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return null;
        }

        var frontmatter = normalizedMarkdown[4..endIndex];
        var content = normalizedMarkdown[(endIndex + "\n---\n".Length)..].Trim();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in frontmatter.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                return null;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (key.Length == 0)
            {
                return null;
            }

            values[key] = value;
        }

        if (!values.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        values.TryGetValue("title", out var title);
        values.TryGetValue("priority", out var priorityText);
        var priority = int.TryParse(priorityText, out var parsedPriority)
            ? parsedPriority
            : 0;

        return new AgentQSystemSkill
        {
            Id = id.Trim(),
            Title = string.IsNullOrWhiteSpace(title) ? id.Trim() : title.Trim(),
            Priority = priority,
            TaskKinds = ParseCsv(values.GetValueOrDefault("taskKinds")),
            Triggers = ParseCsv(values.GetValueOrDefault("triggers")),
            Excludes = ParseCsv(values.GetValueOrDefault("excludes")),
            Content = content,
            Source = source
        };
    }

    private static IReadOnlyList<string> ParseCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static bool IsRelevant(AgentQSystemSkill skill, string normalizedUserText, DesktopTaskProfile taskProfile)
    {
        if (!TaskKindMatches(skill, taskProfile))
        {
            return false;
        }

        if (skill.Excludes.Any(exclude => ContainsNormalized(normalizedUserText, exclude)))
        {
            return false;
        }

        return skill.Triggers.Any(trigger => ContainsNormalized(normalizedUserText, trigger));
    }

    private static bool TaskKindMatches(AgentQSystemSkill skill, DesktopTaskProfile taskProfile)
    {
        if (skill.TaskKinds.Count == 0)
        {
            return true;
        }

        var kind = Normalize(taskProfile.Kind.ToString());
        var label = Normalize(taskProfile.Label);
        return skill.TaskKinds.Any(taskKind =>
        {
            var normalized = Normalize(taskKind);
            return normalized == kind || normalized == label;
        });
    }

    private static bool ContainsNormalized(string normalizedText, string candidate)
    {
        var normalizedCandidate = Normalize(candidate);
        return normalizedCandidate.Length > 0 &&
               normalizedText.Contains(normalizedCandidate, StringComparison.Ordinal);
    }

    private static bool ContainsAnyNormalized(string normalizedText, params string[] candidates) =>
        candidates.Any(candidate => ContainsNormalized(normalizedText, candidate));

    private string Truncate(string content)
    {
        if (content.Length <= _maxSkillContentChars)
        {
            return content;
        }

        return content[.._maxSkillContentChars].TrimEnd() + "\n... truncated ...";
    }

    internal static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch) && ch != '-' && ch != '_')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }
}
