using System.Text.Json;
using AgentQ.Core.Providers;
using AgentQ.Tools;
using Xunit;

namespace AgentQ.Tests;

public sealed class ToolAndConfigurationTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    [Fact]
    public async Task ReadFileTool_ParsesStringOffsetAndLimit()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("CLAW_WORKSPACE_ROOT", workspace.RootPath);
        var filePath = workspace.CreateFile(
            "sample.txt",
            """
            first
            second
            third
            fourth
            """);

        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["offset"] = "2",
            ["limit"] = "2"
        });

        Assert.False(result.IsError);

        using var json = JsonDocument.Parse(result.Content);
        Assert.Equal("second\nthird", json.RootElement.GetProperty("content").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("readLines").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("offset").GetInt32());
    }

    [Fact]
    public async Task EditFileTool_ReplaceAllReportsActualReplacementCount()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("CLAW_WORKSPACE_ROOT", workspace.RootPath);
        var filePath = workspace.CreateFile("sample.txt", "alpha beta alpha");

        var tool = new EditFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["old_string"] = "alpha",
            ["new_string"] = "omega",
            ["replace_all"] = "true"
        });

        Assert.False(result.IsError);
        Assert.Equal("omega beta omega", File.ReadAllText(filePath));

        using var json = JsonDocument.Parse(result.Content);
        Assert.Equal(2, json.RootElement.GetProperty("replacements").GetInt32());
    }

    [Fact]
    public async Task GlobTool_MatchesNestedGlobPatterns()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateFile(Path.Combine("config", "appsettings.json"), "{}");
        workspace.CreateFile(Path.Combine("bin", "skip.json"), "{}");
        workspace.CreateFile(Path.Combine("config", "notes.txt"), "text");

        var tool = new GlobTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = workspace.RootPath,
            ["pattern"] = "**/*.json"
        });

        Assert.False(result.IsError);

        using var json = JsonDocument.Parse(result.Content);
        var files = json.RootElement.GetProperty("files")
            .EnumerateArray()
            .Select(e => e.GetString())
            .OfType<string>()
            .ToArray();

        Assert.Single(files);
        Assert.Contains(files, file => file.Replace('\\', '/').EndsWith("/config/appsettings.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GrepTool_DoesNotExcludeOneDrivePaths()
    {
        using var workspace = new TemporaryWorkspace("OneDrive");
        workspace.CreateFile(Path.Combine("nested", "match.txt"), "needle");

        var tool = new GrepTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = workspace.RootPath,
            ["pattern"] = "needle",
            ["output_mode"] = "count",
            ["include"] = "*.txt"
        });

        Assert.False(result.IsError);

        using var json = JsonDocument.Parse(result.Content);
        Assert.Equal(1, json.RootElement.GetProperty("numMatches").GetInt32());
    }

    [Fact]
    public async Task PluginEchoTool_ReturnsPluginStylePayload()
    {
        var tool = new PluginEchoTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["message"] = "hello"
        });

        Assert.False(result.IsError);

        using var json = JsonDocument.Parse(result.Content);
        Assert.Equal("hello", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("hello", json.RootElement.GetProperty("input").GetProperty("message").GetString());
    }

    [Fact]
    public void ProviderConfiguration_FromArgs_UsesEnvironmentFallbackForProvider()
    {
        SetEnvironment("CLAW_PROVIDER", "openai");
        SetEnvironment("CLAW_MODEL", "gpt-4.1");
        SetEnvironment("CLAW_BASE_URL", "https://example.test");
        SetEnvironment("CLAW_API_KEY", "secret");

        var config = ProviderConfiguration.FromArgs([]);

        Assert.Equal("openai", config.Provider);
        Assert.Equal("gpt-4.1", config.Model);
        Assert.Equal("https://example.test", config.BaseUrl);
        Assert.Equal("secret", config.ApiKey);
    }

    [Fact]
    public void ProviderConfiguration_FromArgs_PrefersExplicitArguments()
    {
        SetEnvironment("CLAW_PROVIDER", "anthropic");
        SetEnvironment("CLAW_MODEL", "claude");

        var config = ProviderConfiguration.FromArgs(["--provider", "openai", "--model", "gpt-5"]);

        Assert.Equal("openai", config.Provider);
        Assert.Equal("gpt-5", config.Model);
    }

    [Fact]
    public async Task ReadFileTool_RejectsPathsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("CLAW_WORKSPACE_ROOT", workspace.RootPath);

        var outsideFile = outside.CreateFile("outside.txt", "blocked");
        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = outsideFile
        });

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteFileTool_RejectsPathsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("CLAW_WORKSPACE_ROOT", workspace.RootPath);

        var outsideFile = Path.Combine(outside.RootPath, "outside.txt");
        var tool = new WriteFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = outsideFile,
            ["content"] = "blocked"
        });

        Assert.True(result.IsError);
        Assert.False(File.Exists(outsideFile));
        Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditFileTool_RejectsPathsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("CLAW_WORKSPACE_ROOT", workspace.RootPath);

        var outsideFile = outside.CreateFile("outside.txt", "alpha");
        var tool = new EditFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = outsideFile,
            ["old_string"] = "alpha",
            ["new_string"] = "omega"
        });

        Assert.True(result.IsError);
        Assert.Equal("alpha", File.ReadAllText(outsideFile));
        Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private void SetEnvironment(string name, string? value)
    {
        if (!_originalEnvironment.ContainsKey(name))
        {
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        foreach (var pair in _originalEnvironment)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace(string? rootName = null)
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                rootName ?? "AgentQ.Tests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateFile(string relativePath, string content)
        {
            var path = Path.Combine(RootPath, relativePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content.Replace("\n", Environment.NewLine));
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}

