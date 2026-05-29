namespace AgentQ.Desktop.Services;

public sealed class ProjectMemoryGcService
{
    public ProjectMemoryGcReport Preview(
        IReadOnlyList<ProjectMemoryLesson> lessons,
        ProjectMemoryGcOptions? options = null)
    {
        options ??= new ProjectMemoryGcOptions();
        var removals = FindRemovals(lessons, options);
        return new ProjectMemoryGcReport
        {
            BeforeCount = lessons.Count,
            AfterCount = Math.Max(0, lessons.Count - removals.Count),
            RemovedLessons = removals
        };
    }

    public ProjectMemoryGcReport Apply(
        List<ProjectMemoryLesson> lessons,
        ProjectMemoryGcOptions? options = null)
    {
        var report = Preview(lessons, options);
        var removeIds = report.RemovedLessons
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        lessons.RemoveAll(lesson => removeIds.Contains(lesson.Id));
        return new ProjectMemoryGcReport
        {
            BeforeCount = report.BeforeCount,
            AfterCount = lessons.Count,
            RemovedLessons = report.RemovedLessons
        };
    }

    private static List<ProjectMemoryGcItem> FindRemovals(
        IReadOnlyList<ProjectMemoryLesson> lessons,
        ProjectMemoryGcOptions options)
    {
        var removals = new Dictionary<string, ProjectMemoryGcItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var lesson in lessons)
        {
            if (string.IsNullOrWhiteSpace(lesson.Id))
            {
                continue;
            }

            if (!lesson.Enabled)
            {
                AddRemoval(removals, lesson, "disabled");
                continue;
            }

            if (lesson.ExpiresAt.HasValue && lesson.ExpiresAt.Value <= DateTime.Now)
            {
                AddRemoval(removals, lesson, "expired");
                continue;
            }

            if (lesson.Confidence < options.MinimumConfidence)
            {
                AddRemoval(removals, lesson, "low confidence");
                continue;
            }

            var lastRelevantAt = lesson.LastUsedAt ?? lesson.CreatedAt;
            if (lastRelevantAt != default && lastRelevantAt <= DateTime.Now.AddDays(-options.ExpireUnusedAfterDays))
            {
                AddRemoval(removals, lesson, "stale unused");
            }
        }

        foreach (var duplicate in FindDuplicateRemovals(lessons, removals.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)))
        {
            AddRemoval(removals, duplicate, "duplicate");
        }

        foreach (var overflow in FindOverflowRemovals(lessons, removals.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), options.MaxLessons))
        {
            AddRemoval(removals, overflow, "over memory limit");
        }

        return removals.Values
            .OrderBy(item => item.Reason)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<ProjectMemoryLesson> FindDuplicateRemovals(
        IReadOnlyList<ProjectMemoryLesson> lessons,
        IReadOnlySet<string> alreadyRemoved)
    {
        return lessons
            .Where(lesson => !alreadyRemoved.Contains(lesson.Id))
            .GroupBy(CreateDuplicateKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .SelectMany(group => group
                .OrderByDescending(lesson => lesson.Confidence)
                .ThenByDescending(lesson => lesson.LastUsedAt ?? lesson.CreatedAt)
                .Skip(1));
    }

    private static IEnumerable<ProjectMemoryLesson> FindOverflowRemovals(
        IReadOnlyList<ProjectMemoryLesson> lessons,
        IReadOnlySet<string> alreadyRemoved,
        int maxLessons)
    {
        if (maxLessons <= 0)
        {
            return [];
        }

        return lessons
            .Where(lesson => !alreadyRemoved.Contains(lesson.Id))
            .OrderByDescending(lesson => lesson.Confidence)
            .ThenByDescending(lesson => lesson.LastUsedAt ?? lesson.CreatedAt)
            .Skip(maxLessons);
    }

    private static string CreateDuplicateKey(ProjectMemoryLesson lesson)
    {
        if (!string.IsNullOrWhiteSpace(lesson.FailureFingerprint))
        {
            return $"failure:{lesson.FailureFingerprint.Trim()}";
        }

        var seed = string.IsNullOrWhiteSpace(lesson.Title)
            ? lesson.Content
            : $"{lesson.Title}\n{lesson.Content}";
        return string.Join(' ', seed.Trim().ToLowerInvariant().Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static void AddRemoval(
        Dictionary<string, ProjectMemoryGcItem> removals,
        ProjectMemoryLesson lesson,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(lesson.Id) || removals.ContainsKey(lesson.Id))
        {
            return;
        }

        removals[lesson.Id] = new ProjectMemoryGcItem
        {
            Id = lesson.Id,
            Title = string.IsNullOrWhiteSpace(lesson.Title) ? lesson.Id : lesson.Title,
            Reason = reason
        };
    }
}
