using System.Text.Json;
using AgentQ.Core.Providers;
using AgentQ.Tools;
using Xunit;

namespace AgentQ.Tests;

/// <summary>
/// 도구 및 구성 설정에 대한 단위 테스트 클래스입니다.
/// </summary>
public sealed class ToolAndConfigurationTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    /// <summary>
    /// ReadFileTool이 문자열 offset과 limit 파라미터를 올바르게 파싱하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ReadFileTool_ParsesStringOffsetAndLimit()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
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

    /// <summary>
    /// EditFileTool이 replace_all 모드에서 실제 교체 횟수를 올바르게 보고하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task EditFileTool_ReplaceAllReportsActualReplacementCount()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
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

    /// <summary>
    /// GlobTool이 중첩 glob 패턴과 일치하는 파일을 올바르게 찾는지 검증합니다.
    /// </summary>
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

    /// <summary>
    /// GrepTool이 OneDrive 경로를 제외하지 않고 올바르게 검색하는지 검증합니다.
    /// </summary>
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

    /// <summary>
    /// PluginEchoTool이 plugin 스타일 페이로드를 올바르게 반환하는지 검증합니다.
    /// </summary>
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

    /// <summary>
    /// ProviderConfiguration.FromArgs가 환경 변수 폴백을 사용하여 제공자를 올바르게 구성하는지 검증합니다.
    /// </summary>
    [Fact]
    public void ProviderConfiguration_FromArgs_UsesEnvironmentFallbackForProvider()
    {
        SetEnvironment("AGENTQ_PROVIDER", "openai");
        SetEnvironment("AGENTQ_MODEL", "gpt-4.1");
        SetEnvironment("AGENTQ_BASE_URL", "https://example.test");
        SetEnvironment("AGENTQ_API_KEY", "secret");

        var config = ProviderConfiguration.FromArgs([]);

        Assert.Equal("openai", config.Provider);
        Assert.Equal("gpt-4.1", config.Model);
        Assert.Equal("https://example.test", config.BaseUrl);
        Assert.Equal("secret", config.ApiKey);
    }

    /// <summary>
    /// ProviderConfiguration.FromArgs가 명시적 인수가 환경 변수보다 우선하는지 검증합니다.
    /// </summary>
    [Fact]
    public void ProviderConfiguration_FromArgs_PrefersExplicitArguments()
    {
        SetEnvironment("AGENTQ_PROVIDER", "anthropic");
        SetEnvironment("AGENTQ_MODEL", "claude");

        var config = ProviderConfiguration.FromArgs(["--provider", "openai", "--model", "gpt-5"]);

        Assert.Equal("openai", config.Provider);
        Assert.Equal("gpt-5", config.Model);
    }

    /// <summary>
    /// ReadFileTool이 작업 공간 루트 외부의 파일 경로를 거부하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task ReadFileTool_RejectsPathsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

        var outsideFile = outside.CreateFile("outside.txt", "blocked");
        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = outsideFile
        });

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// WriteFileTool이 작업 공간 루트 외부의 파일 경로를 거부하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task WriteFileTool_RejectsPathsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

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

    /// <summary>
    /// EditFileTool이 작업 공간 루트 외부의 파일 경로를 거부하는지 검증합니다.
    /// </summary>
    [Fact]
    public async Task EditFileTool_RejectsPathsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

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

    /// <summary>
    /// 환경 변수를 설정하고 원래 값을 추적합니다.
    /// </summary>
    private void SetEnvironment(string name, string? value)
    {
        if (!_originalEnvironment.ContainsKey(name))
        {
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }

        /// <summary>
        /// 임시 디렉토리와 모든 내용을 삭제합니다.
        /// </summary>
        public void Dispose()
    {
        foreach (var pair in _originalEnvironment)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    /// <summary>
    /// 임시 작업 공간 디렉토리를 관리하는 헬퍼 클래스입니다.
    /// </summary>
    private sealed class TemporaryWorkspace : IDisposable
    {
        /// <summary>
        /// 지정된 이름으로 임시 작업 공간 디렉토리를 생성합니다.
        /// </summary>
        public TemporaryWorkspace(string? rootName = null)
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                rootName ?? "AgentQ.Tests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(RootPath);
        }

        /// <summary>
        /// 임시 작업 공간의 루트 디렉토리 경로입니다.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// 지정된 상대 경로와 내용으로 파일을 생성합니다.
        /// </summary>
        public string CreateFile(string relativePath, string content)
        {
            var path = Path.Combine(RootPath, relativePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
            return path;
        }

    /// <summary>
    /// 테스트 중 변경된 환경 변수를 원래 값으로 복원합니다.
    /// </summary>
    public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}

