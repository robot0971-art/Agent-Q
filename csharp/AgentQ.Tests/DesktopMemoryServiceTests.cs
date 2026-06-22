using AgentQ.Desktop.Services;
using Xunit;

namespace AgentQ.Tests;

public sealed class DesktopMemoryServiceTests
{
    [Fact]
    public void ProjectMemoryService_BuildContext_LabelsMemoryAsHistoricalEvidence()
    {
        var service = new ProjectMemoryService();
        var memory = new ProjectMemory
        {
            WorkspaceRoot = "C:\\repo",
            Lessons =
            [
                new ProjectMemoryLesson
                {
                    Id = "folder-create",
                    Title = "Folder creation",
                    Content = "For folder creation requests, create the explicit directory and report the path.",
                    Tags = ["folder", "create"],
                    Confidence = 0.9,
                    CreatedAt = DateTime.Now,
                    Source = "test"
                }
            ]
        };

        var context = service.BuildContext(memory, "create test2 folder");

        Assert.Contains("Historical project memory only", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Folder creation", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectMemoryService_MergesLessonsByFailureFingerprintAndKeepsLocalOverride()
    {
        var root = CreateTempDirectory();
        var agentQDirectory = Path.Combine(root, ".agentq");
        Directory.CreateDirectory(agentQDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(agentQDirectory, "memory.shared.json"),
            """
            {
              "version": 1,
              "lessons": [
                {
                  "id": "shared-build-failure",
                  "title": "Shared build failure",
                  "content": "Use npm test for this old package.",
                  "failureFingerprint": "build-output-lock",
                  "tags": [ "shared", "test" ],
                  "confidence": 0.95,
                  "source": "shared"
                }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(agentQDirectory, "memory.local.json"),
            """
            {
              "version": 1,
              "lessons": [
                {
                  "id": "local-build-failure",
                  "title": "Local build failure",
                  "content": "Close AgentQ.Desktop.exe before running dotnet test because it can lock build outputs.",
                  "failureFingerprint": "build-output-lock",
                  "tags": [ "local", "desktop" ],
                  "confidence": 0.7,
                  "source": "local"
                }
              ]
            }
            """);

        var service = new ProjectMemoryService();
        var memory = await service.LoadOrDiscoverAsync(root, CancellationToken.None);
        var lesson = Assert.Single(memory.Lessons, lesson => lesson.FailureFingerprint == "build-output-lock");

        Assert.Equal("local-build-failure", lesson.Id);
        Assert.Contains("AgentQ.Desktop.exe", lesson.Content, StringComparison.Ordinal);
        Assert.Contains("local", lesson.Tags);
        Assert.Contains("shared", lesson.Tags);
        Assert.Equal(0.95, lesson.Confidence);
    }

    [Fact]
    public void ProjectMemoryService_BuildContext_DecaysOldUnusedLessonsBelowUsefulThreshold()
    {
        var service = new ProjectMemoryService();
        var memory = new ProjectMemory
        {
            WorkspaceRoot = "C:\\repo",
            Lessons =
            [
                new ProjectMemoryLesson
                {
                    Id = "old",
                    Title = "Old marginal lesson",
                    Content = "This old marginal memory should not be used.",
                    Confidence = 0.3,
                    CreatedAt = DateTime.Now.AddDays(-120),
                    Source = "test"
                },
                new ProjectMemoryLesson
                {
                    Id = "fresh",
                    Title = "Fresh lesson",
                    Content = "This fresh memory should be used.",
                    Confidence = 0.3,
                    CreatedAt = DateTime.Now,
                    Source = "test"
                }
            ]
        };

        var context = service.BuildContext(memory);

        Assert.DoesNotContain("old marginal memory", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh memory", context, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
