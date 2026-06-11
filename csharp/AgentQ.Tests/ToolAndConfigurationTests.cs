using System.Text.Json;
using AgentQ.Cli;
using AgentQ.Core.Providers;
using AgentQ.Desktop.Services;
using AgentQ.Tools;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentQ.Tests;

/// <summary>
/// ?꾧뎄 諛?援ъ꽦 ?ㅼ젙??????⑥쐞 ?뚯뒪???대옒?ㅼ엯?덈떎.
/// </summary>
[Collection("Environment variable tests")]
public sealed class ToolAndConfigurationTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalEnvironment = new();
    private readonly string _configHomeRoot;
    private readonly string _configDirectory;
    private readonly string _configPath;

    public ToolAndConfigurationTests()
    {
        _configHomeRoot = Path.Combine(
            Path.GetTempPath(),
            "AgentQ.ConfigHome",
            Guid.NewGuid().ToString("N"));
        SetEnvironment(ConfigStore.ConfigHomeEnvironmentVariable, _configHomeRoot);
        _configDirectory = Path.Combine(_configHomeRoot, ".agentq");
        _configPath = Path.Combine(_configDirectory, "config.json");
    }

    [Fact]
    public async Task SessionStore_SaveAndLoad_RoundTripsConversationHistory()
    {
        using var workspace = new TemporaryWorkspace();
        var sessionPath = Path.Combine(workspace.RootPath, "session.json");

        var messages = new List<AgentQ.Core.Models.ChatMessage>
        {
            AgentQ.Core.Models.ChatMessage.SystemText("system prompt"),
            AgentQ.Core.Models.ChatMessage.UserText("user input"),
            new()
            {
                Role = AgentQ.Core.Models.ChatRole.Assistant,
                Content =
                [
                    AgentQ.Core.Models.ChatContent.CreateText("Thinking"),
                    AgentQ.Core.Models.ChatContent.CreateToolUse("tool_1", "read_file", "{\"path\":\"sample.txt\"}")
                ]
            },
            AgentQ.Core.Models.ChatMessage.UserToolResult("tool_1", "{\"content\":\"sample\"}", false)
        };

        await SessionStore.SaveAsync(sessionPath, messages);
        var loaded = await SessionStore.LoadAsync(sessionPath);

        Assert.Equal(4, loaded.Count);
        Assert.Equal(AgentQ.Core.Models.ChatRole.System, loaded[0].Role);
        Assert.Equal("system prompt", Assert.Single(loaded[0].Content).Text);
        Assert.Equal(AgentQ.Core.Models.ChatRole.Assistant, loaded[2].Role);
        Assert.Equal(2, loaded[2].Content.Count);
        Assert.Equal("read_file", Assert.Single(loaded[2].Content, content => content.Type == AgentQ.Core.Models.ContentType.ToolUse).ToolName);
        Assert.Equal("{\"content\":\"sample\"}", Assert.Single(loaded[3].Content).ToolResult);
    }

    [Fact]
    public async Task SessionStore_LoadAsync_ThrowsForMissingFile()
    {
        using var workspace = new TemporaryWorkspace();
        var missingPath = Path.Combine(workspace.RootPath, "missing-session.json");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => SessionStore.LoadAsync(missingPath));

        Assert.Contains("Session file not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRegistry_RegisterRejectsDuplicateToolNames()
    {
        var registry = new ToolRegistry();
        registry.Register(new NamedTestTool("same_tool", "first"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register(new NamedTestTool("same_tool", "second")));

        Assert.Contains("same_tool", exception.Message, StringComparison.Ordinal);
        Assert.Equal("first", registry.Get("same_tool")?.Description);
    }

    [Fact]
    public void ToolRegistry_TryRegisterRecordsDuplicateWithoutReplacing()
    {
        var registry = new ToolRegistry();
        registry.Register(new NamedTestTool("same_tool", "first"));

        var registered = registry.TryRegister(new NamedTestTool("same_tool", "second"));

        Assert.False(registered);
        Assert.Equal("first", registry.Get("same_tool")?.Description);
        Assert.Contains("same_tool", registry.DuplicateRegistrations);
    }

    [Fact]
    public async Task ListDirectoryTool_ListsWorkspaceEntries()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "src"));
        await File.WriteAllTextAsync(Path.Combine(workspace.RootPath, "README.md"), "hello");

        var result = await new ListDirectoryTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = "."
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var root = document.RootElement;
        Assert.False(root.GetProperty("isEmpty").GetBoolean());
        var entries = root.GetProperty("entries").EnumerateArray().ToList();
        Assert.Contains(entries, entry =>
            entry.GetProperty("name").GetString() == "src" &&
            entry.GetProperty("type").GetString() == "directory");
        Assert.Contains(entries, entry =>
            entry.GetProperty("name").GetString() == "README.md" &&
            entry.GetProperty("type").GetString() == "file");
    }

    [Fact]
    public async Task ListDirectoryTool_AcceptsJsonElementStringPath()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, "src"));

        var result = await new ListDirectoryTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = JsonSerializer.SerializeToElement(".")
        });

        Assert.False(result.IsError);
        Assert.Contains("src", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListDirectoryTool_HidesDotPrefixedEntriesByDefault()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, ".agentq"));
        await File.WriteAllTextAsync(Path.Combine(workspace.RootPath, ".env"), "secret");

        var result = await new ListDirectoryTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = "."
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var root = document.RootElement;
        Assert.True(root.GetProperty("isEmpty").GetBoolean());
        var entries = root.GetProperty("entries").EnumerateArray().ToList();
        Assert.DoesNotContain(entries, entry => entry.GetProperty("name").GetString() == ".agentq");
        Assert.DoesNotContain(entries, entry => entry.GetProperty("name").GetString() == ".env");
    }

    [Fact]
    public async Task ListDirectoryTool_IncludesDotPrefixedEntriesWhenRequested()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        Directory.CreateDirectory(Path.Combine(workspace.RootPath, ".agentq"));

        var result = await new ListDirectoryTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = ".",
            ["includeHidden"] = true
        });

        Assert.False(result.IsError, result.ErrorMessage);
        using var document = JsonDocument.Parse(result.Content);
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Assert.Contains(entries, entry => entry.GetProperty("name").GetString() == ".agentq");
    }

    [Fact]
    public async Task ListDirectoryTool_BlocksPathsOutsideWorkspace()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

        var result = await new ListDirectoryTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = ".."
        });

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigStore_SaveAndLoad_RoundTripsProviderConfiguration()
    {
        var config = new ProviderConfiguration
        {
            Provider = "openai",
            Model = "gpt-5",
            BaseUrl = "https://example.test",
            ApiKey = "secret",
            TimeoutSeconds = 45,
            DesktopEnableScreenshotLlmVisionReview = true
        };

        await ConfigStore.SaveAsync(config);
        var loaded = await ConfigStore.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("openai", loaded!.Provider);
        Assert.Equal("gpt-5", loaded.Model);
        Assert.Equal("https://example.test", loaded.BaseUrl);
        Assert.Equal("secret", loaded.ApiKey);
        Assert.Equal(45, loaded.TimeoutSeconds);
        Assert.True(loaded.DesktopEnableScreenshotLlmVisionReview);
        Assert.Equal(4096u, loaded.MaxTokens);
        Assert.True(ConfigStore.Exists);
        Assert.EndsWith(Path.Combine(".agentq", "config.json"), ConfigStore.PathValue, StringComparison.OrdinalIgnoreCase);

        var savedJson = await File.ReadAllTextAsync(ConfigStore.PathValue);
        if (OperatingSystem.IsWindows())
        {
            Assert.DoesNotContain("\"ApiKey\": \"secret\"", savedJson, StringComparison.Ordinal);
            Assert.Contains(ProviderConfigurationSecrets.ProtectedPrefix, savedJson, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ConfigStore_UsesConfigHomeOverride()
    {
        using var configHome = new TemporaryWorkspace("AgentQ.ConfigHome");
        SetEnvironment(ConfigStore.ConfigHomeEnvironmentVariable, configHome.RootPath);

        var config = new ProviderConfiguration
        {
            Provider = "openai",
            Model = "gpt-4.1",
            BaseUrl = "https://example.test",
            ApiKey = "secret"
        };

        await ConfigStore.SaveAsync(config);

        var expectedPath = Path.Combine(configHome.RootPath, ".agentq", "config.json");
        Assert.Equal(expectedPath, ConfigStore.PathValue);
        Assert.True(File.Exists(expectedPath));
        Assert.True(ConfigStore.Exists);
    }

    [Fact]
    public async Task ConfigStore_SaveAsync_ReplacesExistingFileWithoutLeavingTempFiles()
    {
        Directory.CreateDirectory(_configDirectory);
        await File.WriteAllTextAsync(_configPath, "{\"provider\":\"old\"}");

        var config = new ProviderConfiguration
        {
            Provider = "opencode-go",
            Model = "kimi-k2.6",
            BaseUrl = ProviderConfiguration.OpenCodeGoDefaultBaseUrl,
            ApiKey = "secret",
            TimeoutSeconds = 0,
            MaxTokens = 8192
        };

        await ConfigStore.SaveAsync(config);

        var loaded = await ConfigStore.LoadAsync();
        var tempFiles = Directory.GetFiles(_configDirectory, "config.*.tmp");

        Assert.NotNull(loaded);
        Assert.Equal("opencode-go", loaded!.Provider);
        Assert.Equal("kimi-k2.6", loaded.Model);
        Assert.Equal(0, loaded.TimeoutSeconds);
        Assert.Equal(8192u, loaded.MaxTokens);
        Assert.Empty(tempFiles);
    }

    [Fact]
    public async Task ConfigStore_LoadAsync_ReturnsNullForMalformedJson()
    {
        Directory.CreateDirectory(_configDirectory);
        await File.WriteAllTextAsync(_configPath, "{invalid json");

        var loaded = await ConfigStore.LoadAsync();

        Assert.Null(loaded);
        Assert.True(ConfigStore.Exists);
    }

    [Fact]
    public async Task ConfigStore_Delete_RemovesSavedConfiguration()
    {
        var config = new ProviderConfiguration
        {
            Provider = "openai",
            Model = "gpt-4o-mini",
            BaseUrl = "https://api.openai.com/v1/",
            ApiKey = "secret"
        };

        await ConfigStore.SaveAsync(config);
        Assert.True(ConfigStore.Exists);

        ConfigStore.Delete();

        Assert.False(ConfigStore.Exists);
        Assert.Null(await ConfigStore.LoadAsync());
    }

    [Fact]
    public void AddAgentQCli_RegistersRuntimeStorageServices()
    {
        var services = new ServiceCollection();

        services.AddAgentQCli([]);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<FileConfigStore>(provider.GetRequiredService<IConfigStore>());
        Assert.IsType<FileSessionStore>(provider.GetRequiredService<ISessionStore>());
        Assert.IsType<InputFileReader>(provider.GetRequiredService<IInputFileReader>());
        Assert.IsType<ProviderHttpClientFactory>(provider.GetRequiredService<IProviderHttpClientFactory>());
        Assert.IsType<CliAutomationOutput>(provider.GetRequiredService<ICliAutomationOutput>());
        Assert.NotNull(provider.GetRequiredService<CliNonInteractiveRunner>());
        Assert.NotNull(provider.GetRequiredService<CliInteractivePersistenceCommands>());
        Assert.NotNull(provider.GetRequiredService<CliInteractiveSettingsCommands>());
        Assert.NotNull(provider.GetRequiredService<CliInteractiveToolCommands>());
        Assert.NotNull(provider.GetRequiredService<CliInteractiveSessionCommands>());
        Assert.NotNull(provider.GetRequiredService<CliInteractivePresenter>());
        Assert.NotNull(provider.GetRequiredService<CliInteractiveConversationRunner>());
        Assert.NotNull(provider.GetRequiredService<CliConfigurationLoader>());
        Assert.NotNull(provider.GetRequiredService<CliApplication>());

        var registry = provider.GetRequiredService<ToolRegistry>();
        Assert.NotNull(registry.Get("list_directory"));
        Assert.NotNull(registry.Get("create_directory"));
        Assert.NotNull(registry.Get("delete_path"));
        Assert.NotNull(registry.Get("web_search"));
    }

    [Fact]
    public void ConsolePermissionEnforcer_BuildSummary_AcceptsStringWrappedJsonArguments()
    {
        var method = typeof(ConsolePermissionEnforcer).GetMethod(
            "BuildSummary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var result = (IEnumerable<string>?)method!.Invoke(null, ["bash", "\"{\\\"command\\\":\\\"echo hello\\\",\\\"timeout\\\":5000}\""]);
        var summaryLines = Assert.IsAssignableFrom<IEnumerable<string>>(result).ToArray();

        Assert.Contains(summaryLines, line => line.Contains("Command:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summaryLines, line => line.Contains("echo hello", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summaryLines, line => line.Contains("5000", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConsolePermissionEnforcer_SessionAllowedTools_ShowsOnlyReusablePermissions()
    {
        var enforcer = new ConsolePermissionEnforcer();
        var field = typeof(ConsolePermissionEnforcer).GetField(
            "_sessionAllowedTools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(field);

        var allowedTools = Assert.IsType<HashSet<string>>(field!.GetValue(enforcer));
        allowedTools.Add("bash");
        allowedTools.Add("read_file");
        allowedTools.Add("web_search");

        Assert.Equal(["web_search"], enforcer.SessionAllowedTools.ToArray());

        enforcer.ClearSessionAllowedTools();

        Assert.Empty(enforcer.SessionAllowedTools);
    }

    [Theory]
    [InlineData("bash", false)]
    [InlineData("write_file", false)]
    [InlineData("edit_file", false)]
    [InlineData("create_directory", false)]
    [InlineData("delete_path", false)]
    [InlineData("web_search", true)]
    public void ConsolePermissionEnforcer_OnlyReadOnlySearchIsSessionReusable(string toolName, bool expected)
    {
        Assert.Equal(expected, ConsolePermissionEnforcer.IsSessionReusableTool(toolName));
    }

    [Fact]
    public async Task CliInteractiveToolCommands_ParsesJsonBeforePermissionRequest()
    {
        var registry = new ToolRegistry();
        registry.Register(new NamedTestTool("secure_tool", "test secure tool", requiresPermission: true));
        var commands = new CliInteractiveToolCommands();

        await commands.RunToolAsync(
            registry,
            new ThrowingPermissionEnforcer(),
            new CliToolLoopRunner(),
            "secure_tool {\"path\":");
    }

    /// <summary>
    /// ReadFileTool??臾몄옄??offset怨?limit ?뚮씪誘명꽣瑜??щ컮瑜닿쾶 ?뚯떛?섎뒗吏 寃利앺빀?덈떎.
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

    [Fact]
    public async Task ReadFileTool_ClampsLargeLimit_AndReportsTruncation()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        var filePath = workspace.CreateFile(
            "large.txt",
            string.Join('\n', Enumerable.Range(1, 800).Select(i => new string('a', 80) + i)));

        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["limit"] = 1000
        });

        Assert.False(result.IsError);

        using var json = JsonDocument.Parse(result.Content);
        Assert.Equal(500, json.RootElement.GetProperty("limit").GetInt32());
        Assert.True(json.RootElement.GetProperty("limitClamped").GetBoolean());
        Assert.True(json.RootElement.GetProperty("contentTruncated").GetBoolean());
    }

    [Fact]
    public async Task ReadFileTool_AcceptsJsonElementStringPath()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        workspace.CreateFile("sample.txt", "hello");
        var pathElement = JsonSerializer.SerializeToElement("sample.txt");

        var result = await new ReadFileTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = pathElement
        });

        Assert.False(result.IsError);
        Assert.Contains("hello", result.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// EditFileTool??replace_all 紐⑤뱶?먯꽌 ?ㅼ젣 援먯껜 ?잛닔瑜??щ컮瑜닿쾶 蹂닿퀬?섎뒗吏 寃利앺빀?덈떎.
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

    [Fact]
    public async Task EditFileTool_AcceptsJsonElementStringArguments()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        var filePath = workspace.CreateFile("sample.txt", "alpha beta");

        var result = await new EditFileTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = JsonSerializer.SerializeToElement("sample.txt"),
            ["old_string"] = JsonSerializer.SerializeToElement("alpha"),
            ["new_string"] = JsonSerializer.SerializeToElement("omega")
        });

        Assert.False(result.IsError);
        Assert.Equal("omega beta".Replace("\n", Environment.NewLine), File.ReadAllText(filePath));
    }

    /// <summary>
    /// EditFileTool???⑥씪 援먯껜 紐⑤뱶?먯꽌 以묐났 留ㅼ튂瑜??덉쟾?섍쾶 嫄곕??섎뒗吏 寃利앺빀?덈떎.
    /// </summary>
    [Fact]
    public async Task EditFileTool_RejectsAmbiguousSingleReplace()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        var filePath = workspace.CreateFile("sample.txt", "alpha beta alpha");

        var tool = new EditFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["old_string"] = "alpha",
            ["new_string"] = "omega"
        });

        Assert.True(result.IsError);
        Assert.Contains("use replace_all=true", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("alpha beta alpha", File.ReadAllText(filePath));
    }

    /// <summary>
    /// EditFileTool??鍮?old_string??嫄곕??섎뒗吏 寃利앺빀?덈떎.
    /// </summary>
    [Fact]
    public async Task EditFileTool_RejectsEmptyOldString()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        var filePath = workspace.CreateFile("sample.txt", "alpha beta");

        var tool = new EditFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["old_string"] = "",
            ["new_string"] = "omega"
        });

        Assert.True(result.IsError);
        Assert.Contains("must not be empty", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditFileTool_RejectsBroadReplaceAllOnUnityBehaviourWithoutApproval()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        var filePath = workspace.CreateFile(
            "DamageFlashController.cs",
            """
            using UnityEngine;

            public sealed class DamageFlashController : MonoBehaviour
            {
                [SerializeField] private Renderer targetRenderer;

                public void Flash()
                {
                    targetRenderer.enabled = true;
                }
            }
            """);

        var tool = new EditFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["old_string"] = "targetRenderer",
            ["new_string"] = "renderer",
            ["replace_all"] = true
        });

        Assert.True(result.IsError);
        Assert.Contains("high-risk", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SerializeField", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("targetRenderer", File.ReadAllText(filePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditFileTool_AllowsSmallPatchOnUnityBehaviour()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        var filePath = workspace.CreateFile(
            "DamageFlashController.cs",
            """
            using UnityEngine;

            public sealed class DamageFlashController : MonoBehaviour
            {
                [SerializeField] private Renderer targetRenderer;

                public void Flash()
                {
                    targetRenderer.enabled = true;
                }
            }
            """);

        var tool = new EditFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["old_string"] = "targetRenderer.enabled = true;",
            ["new_string"] = "targetRenderer.enabled = !targetRenderer.enabled;"
        });

        Assert.False(result.IsError);
        Assert.Contains("!targetRenderer.enabled", File.ReadAllText(filePath), StringComparison.Ordinal);
    }

    /// <summary>
    /// WriteFileTool??overwrite=false????湲곗〈 ?뚯씪 ??뼱?곌린瑜?嫄곕??섎뒗吏 寃利앺빀?덈떎.
    /// </summary>
    [Fact]
    public async Task WriteFileTool_RejectsOverwriteWhenExplicitlyDisabled()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        var filePath = workspace.CreateFile("sample.txt", "original");

        var tool = new WriteFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["content"] = "updated",
            ["overwrite"] = false
        });

        Assert.True(result.IsError);
        Assert.Contains("overwrite=true", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("original", File.ReadAllText(filePath));
    }

    [Fact]
    public async Task WriteFileTool_RejectsWholeFileRewriteOfUnityBehaviourWithoutApproval()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        var filePath = workspace.CreateFile(
            "EnemyHealth.cs",
            """
            using UnityEngine;

            public sealed class EnemyHealth : MonoBehaviour
            {
                [SerializeField] private int maxHealth = 10;
            }
            """);

        var tool = new WriteFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = filePath,
            ["content"] = "public sealed class EnemyHealth { }"
        });

        Assert.True(result.IsError);
        Assert.Contains("whole-file rewrite", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maxHealth", File.ReadAllText(filePath), StringComparison.Ordinal);
    }

    /// <summary>
    /// WriteFileTool???붾젆?좊━ 寃쎈줈瑜??뚯씪 ??곸쑝濡??덉슜?섏? ?딅뒗吏 寃利앺빀?덈떎.
    /// </summary>
    [Fact]
    public async Task WriteFileTool_RejectsDirectoryTargets()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        var directoryPath = Path.Combine(workspace.RootPath, "nested");
        Directory.CreateDirectory(directoryPath);

        var tool = new WriteFileTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = directoryPath,
            ["content"] = "updated"
        });

        Assert.True(result.IsError);
        Assert.Contains("directory", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// BashTool???꾪뿕??紐낅졊 ?⑦꽩??李⑤떒?섎뒗吏 寃利앺빀?덈떎.
    /// </summary>
    [Fact]
    public async Task BashTool_BlocksDangerousCommands()
    {
        var tool = new BashTool();
        var dangerousCommands = new[]
        {
            "rm -rf /",
            "rm -rf .",
            "rm -fr src",
            "rm --recursive --force src",
            "rmdir /s /q C:\\temp\\danger",
            "rmdir -rf src",
            "rd /s /q C:\\temp\\danger",
            "erase /q /s C:\\temp\\danger\\*",
            "del /s /q /f C:\\temp\\danger\\*",
            "powershell -EncodedCommand SQBFAFgA",
            "powershell -enc SQBFAFgA",
            "diskpart /s wipe.txt",
            "fsutil file setzerodata offset=0 length=1024 C:\\temp\\file.bin",
            "cipher /w:C:\\temp",
            "net user demo /delete",
            "Remove-Item C:\\temp -Recurse -Force",
            "ri C:\\temp -r -fo",
            "del C:\\temp -Recurse -Force",
            "takeown /f C:\\temp && rmdir /s /q C:\\temp",
            "icacls C:\\temp /grant Everyone:F && del /s /q C:\\temp\\*"
        };

        foreach (var command in dangerousCommands)
        {
            var result = await tool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["command"] = command
            });

            Assert.True(result.IsError, $"Expected command to be blocked: {command}");
            Assert.Contains("blocked by safety policy", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// BashTool???덉슜 踰붿쐞瑜?踰쀬뼱??timeout 媛믪쓣 嫄곕??섎뒗吏 寃利앺빀?덈떎.
    /// </summary>
    [Fact]
    public async Task BashTool_RejectsTimeoutOutsideAllowedRange()
    {
        var tool = new BashTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["command"] = "Write-Output 'hello'",
            ["timeout"] = 500
        });

        Assert.True(result.IsError);
        Assert.Contains("Timeout must be between", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BashTool_UsesWorkspaceRootAsWorkingDirectory()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        var command = OperatingSystem.IsWindows() ? "Get-Location" : "pwd";

        var tool = new BashTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["command"] = command
        });

        Assert.False(result.IsError);

        using var json = JsonDocument.Parse(result.Content);
        var stdout = json.RootElement.GetProperty("stdout").GetString() ?? string.Empty;
        Assert.Contains(workspace.RootPath, stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BashTool_AcceptsJsonElementStringCommand()
    {
        var command = OperatingSystem.IsWindows() ? "Write-Output 'hello'" : "printf hello";

        var result = await new BashTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["command"] = JsonSerializer.SerializeToElement(command)
        });

        Assert.False(result.IsError);
        Assert.Contains("hello", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BashTool_DescribesWindowsPowerShellSemantics()
    {
        var tool = new BashTool();

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("PowerShell", tool.Description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("workspace root", tool.Description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&&", tool.Description, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("bash command", tool.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// BashTool??湲?異쒕젰???섎씪?닿퀬 ?덈떒 ?щ?瑜?蹂닿퀬?섎뒗吏 寃利앺빀?덈떎.
    /// </summary>
    [Fact]
    public async Task BashTool_TruncatesLongOutput()
    {
        var tool = new BashTool();
        var command = OperatingSystem.IsWindows()
            ? "Write-Output ('a' * 33000)"
            : "python3 - <<'PY'\nprint('a' * 33000)\nPY";

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["command"] = command
        });

        Assert.False(result.IsError);

        using var json = JsonDocument.Parse(result.Content);
        Assert.True(json.RootElement.GetProperty("stdoutTruncated").GetBoolean());
        Assert.False(json.RootElement.GetProperty("stderrTruncated").GetBoolean());
        Assert.Contains("[truncated]", json.RootElement.GetProperty("stdout").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// GlobTool??以묒꺽 glob ?⑦꽩怨??쇱튂?섎뒗 ?뚯씪???щ컮瑜닿쾶 李얜뒗吏 寃利앺빀?덈떎.
    /// </summary>
    [Fact]
    public async Task GlobTool_MatchesNestedGlobPatterns()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
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
    public async Task GlobTool_AcceptsJsonElementStringArguments()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        workspace.CreateFile(Path.Combine("config", "appsettings.json"), "{}");

        var result = await new GlobTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = JsonSerializer.SerializeToElement("."),
            ["pattern"] = JsonSerializer.SerializeToElement("**/*.json")
        });

        Assert.False(result.IsError);
        Assert.Contains("appsettings.json", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GlobTool_RejectsPathsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        outside.CreateFile("sample.txt", "data");

        var tool = new GlobTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = outside.RootPath,
            ["pattern"] = "*.txt"
        });

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GrepTool??OneDrive 寃쎈줈瑜??쒖쇅?섏? ?딄퀬 ?щ컮瑜닿쾶 寃?됲븯?붿? 寃利앺빀?덈떎.
    /// </summary>
    [Fact]
    public async Task GrepTool_DoesNotExcludeOneDrivePaths()
    {
        using var workspace = new TemporaryWorkspace("OneDrive");
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
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
    public async Task GrepTool_AcceptsJsonElementStringArguments()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        workspace.CreateFile(Path.Combine("nested", "match.txt"), "needle");

        var result = await new GrepTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = JsonSerializer.SerializeToElement("."),
            ["pattern"] = JsonSerializer.SerializeToElement("needle"),
            ["output_mode"] = JsonSerializer.SerializeToElement("count"),
            ["include"] = JsonSerializer.SerializeToElement("*.txt")
        });

        Assert.False(result.IsError);
        Assert.Contains("\"numMatches\":1", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GrepTool_RejectsPathsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        outside.CreateFile("match.txt", "needle");

        var tool = new GrepTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = outside.RootPath,
            ["pattern"] = "needle"
        });

        Assert.True(result.IsError);
        Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GrepTool_DoesNotTraverseDirectorySymlinkOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        outside.CreateFile("secret.txt", "needle");
        workspace.CreateFile("visible.txt", "ordinary");

        var linkPath = Path.Combine(workspace.RootPath, "link-out");
        if (!TryCreateDirectorySymbolicLink(linkPath, outside.RootPath))
        {
            return;
        }

        try
        {
            var result = await new GrepTool().ExecuteAsync(new Dictionary<string, object?>
            {
                ["path"] = ".",
                ["pattern"] = "needle",
                ["include"] = "*.txt"
            });

            Assert.False(result.IsError);
            using var json = JsonDocument.Parse(result.Content);
            Assert.Equal(0, json.RootElement.GetProperty("numMatches").GetInt32());
            Assert.DoesNotContain("secret.txt", result.Content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFileSystemLink(linkPath);
        }
    }

    [Fact]
    public async Task GlobTool_DoesNotTraverseDirectorySymlinkOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        outside.CreateFile("secret.json", "{}");
        workspace.CreateFile("visible.txt", "ordinary");

        var linkPath = Path.Combine(workspace.RootPath, "link-out");
        if (!TryCreateDirectorySymbolicLink(linkPath, outside.RootPath))
        {
            return;
        }

        try
        {
            var result = await new GlobTool().ExecuteAsync(new Dictionary<string, object?>
            {
                ["path"] = ".",
                ["pattern"] = "**/*.json"
            });

            Assert.False(result.IsError);
            using var json = JsonDocument.Parse(result.Content);
            Assert.Equal(0, json.RootElement.GetProperty("numFiles").GetInt32());
            Assert.DoesNotContain("secret.json", result.Content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFileSystemLink(linkPath);
        }
    }

    [Fact]
    public async Task GrepTool_StopsAfterMaximumMatches()
    {
        using var workspace = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);
        workspace.CreateFile(
            "match.txt",
            string.Join('\n', Enumerable.Range(1, 300).Select(_ => "needle")));

        var tool = new GrepTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = workspace.RootPath,
            ["pattern"] = "needle"
        });

        Assert.False(result.IsError);

        using var json = JsonDocument.Parse(result.Content);
        Assert.Equal(200, json.RootElement.GetProperty("numMatches").GetInt32());
        Assert.True(json.RootElement.GetProperty("matchLimitReached").GetBoolean());
    }

    /// <summary>
    /// PluginEchoTool??plugin ?ㅽ????섏씠濡쒕뱶瑜??щ컮瑜닿쾶 諛섑솚?섎뒗吏 寃利앺빀?덈떎.
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

    [Fact]
    public async Task WebSearchTool_ClampsHugeMaxResultsWithoutThrowing()
    {
        const string html = """
            <html><body>
            <a class="result__a" href="https://example.com/a">A</a><div class="result__snippet">First</div>
            <a class="result__a" href="https://example.com/b">B</a><div class="result__snippet">Second</div>
            </body></html>
            """;
        using var httpClient = new HttpClient(new StaticResponseHandler(html));
        var tool = new WebSearchTool(httpClient);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["query"] = "agent q",
            ["max_results"] = long.MaxValue
        });

        Assert.False(result.IsError);
        using var json = JsonDocument.Parse(result.Content);
        Assert.Equal(2, json.RootElement.GetProperty("resultCount").GetInt32());
    }

    /// <summary>
    /// ProviderConfiguration.FromArgs媛 ?섍꼍 蹂???대갚???ъ슜?섏뿬 ?쒓났?먮? ?щ컮瑜닿쾶 援ъ꽦?섎뒗吏 寃利앺빀?덈떎.
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

    [Fact]
    public void ProviderConfiguration_FromArgs_LeavesProviderEmptyWithoutExplicitConfiguration()
    {
        SetEnvironment("AGENTQ_PROVIDER", null);
        SetEnvironment("AGENTQ_MODEL", null);
        SetEnvironment("AGENTQ_BASE_URL", null);
        SetEnvironment("AGENTQ_API_KEY", null);
        SetEnvironment("CLAW_PROVIDER", null);
        SetEnvironment("CLAW_MODEL", null);
        SetEnvironment("CLAW_BASE_URL", null);
        SetEnvironment("CLAW_API_KEY", null);
        SetEnvironment("ANTHROPIC_API_KEY", null);
        SetEnvironment("OPENCODE_GO_MODEL", null);
        SetEnvironment("OPENCODE_GO_BASE_URL", null);
        SetEnvironment("OPENCODE_GO_API_KEY", null);

        var config = ProviderConfiguration.FromArgs([]);

        Assert.Equal(string.Empty, config.Provider);
        Assert.Equal(string.Empty, config.BaseUrl);
    }

    [Fact]
    public void ProviderConfiguration_FromArgs_UsesOpenCodeGoEnvironmentFallback()
    {
        SetEnvironment("AGENTQ_PROVIDER", null);
        SetEnvironment("AGENTQ_MODEL", null);
        SetEnvironment("AGENTQ_BASE_URL", null);
        SetEnvironment("AGENTQ_API_KEY", null);
        SetEnvironment("CLAW_PROVIDER", null);
        SetEnvironment("CLAW_MODEL", null);
        SetEnvironment("CLAW_BASE_URL", null);
        SetEnvironment("CLAW_API_KEY", null);
        SetEnvironment("ANTHROPIC_API_KEY", null);
        SetEnvironment("OPENCODE_GO_MODEL", "glm-4.6");
        SetEnvironment("OPENCODE_GO_BASE_URL", "https://opencode-go.example/v1");
        SetEnvironment("OPENCODE_GO_API_KEY", "opencode-secret");

        var config = ProviderConfiguration.FromArgs([]);

        Assert.Equal("opencode-go", config.Provider);
        Assert.Equal("glm-4.6", config.Model);
        Assert.Equal("https://opencode-go.example/v1", config.BaseUrl);
        Assert.Equal("opencode-secret", config.ApiKey);
    }

    [Fact]
    public void ProviderConfiguration_FromArgs_DefaultsOpenCodeGoBaseUrl()
    {
        SetEnvironment("AGENTQ_PROVIDER", null);
        SetEnvironment("AGENTQ_MODEL", null);
        SetEnvironment("AGENTQ_BASE_URL", null);
        SetEnvironment("AGENTQ_API_KEY", null);
        SetEnvironment("CLAW_PROVIDER", null);
        SetEnvironment("CLAW_MODEL", null);
        SetEnvironment("CLAW_BASE_URL", null);
        SetEnvironment("CLAW_API_KEY", null);
        SetEnvironment("ANTHROPIC_API_KEY", null);
        SetEnvironment("OPENCODE_GO_MODEL", "kimi-k2.6");
        SetEnvironment("OPENCODE_GO_API_KEY", "opencode-secret");

        var config = ProviderConfiguration.FromArgs([]);

        Assert.Equal("opencode-go", config.Provider);
        Assert.Equal("kimi-k2.6", config.Model);
        Assert.Equal(ProviderConfiguration.OpenCodeGoDefaultBaseUrl, config.BaseUrl);
        Assert.Equal("opencode-secret", config.ApiKey);
    }

    /// <summary>
    /// ProviderConfiguration.FromArgs媛 紐낆떆???몄닔媛 ?섍꼍 蹂?섎낫???곗꽑?섎뒗吏 寃利앺빀?덈떎.
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

    [Fact]
    public void ProviderConfiguration_FromArgs_ParsesMaxTokens()
    {
        SetEnvironment("AGENTQ_MAX_TOKENS", "8192");

        var envConfig = ProviderConfiguration.FromArgs([]);
        var explicitConfig = ProviderConfiguration.FromArgs(["--max-tokens", "16384"]);

        Assert.Equal(8192u, envConfig.MaxTokens);
        Assert.Equal(16384u, explicitConfig.MaxTokens);
    }

    [Fact]
    public void ProviderConfiguration_FromArgs_IgnoresNegativeTimeoutValues()
    {
        SetEnvironment("AGENTQ_TIMEOUT", "-1");

        var envConfig = ProviderConfiguration.FromArgs([]);
        var explicitConfig = ProviderConfiguration.FromArgs(["--timeout", "-1"]);
        var disabledConfig = ProviderConfiguration.FromArgs(["--timeout", "0"]);

        Assert.Equal(60, envConfig.TimeoutSeconds);
        Assert.Equal(60, explicitConfig.TimeoutSeconds);
        Assert.Equal(0, disabledConfig.TimeoutSeconds);
    }

    [Fact]
    public void ProviderConfiguration_FromArgs_ParsesNonInteractivePromptOptions()
    {
        var config = ProviderConfiguration.FromArgs([
            "--prompt", "summarize",
            "--stdin",
            "--input", "prompt.txt",
            "--json",
            "--yes",
            "--allow-tool", "read_file",
            "--allow-tool", "bash",
            "--deny-tool", "bash"]);

        Assert.Equal("summarize", config.Prompt);
        Assert.True(config.ReadPromptFromStdin);
        Assert.Equal("prompt.txt", config.InputFilePath);
        Assert.True(config.JsonOutput);
        Assert.True(config.AllowToolsWithoutPrompt);
        Assert.Equal(["read_file", "bash"], config.AllowedToolNames);
        Assert.Equal(["bash"], config.DeniedToolNames);
    }

    /// <summary>
    /// ReadFileTool???묒뾽 怨듦컙 猷⑦듃 ?몃????뚯씪 寃쎈줈瑜?嫄곕??섎뒗吏 寃利앺빀?덈떎.
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
    /// WriteFileTool???묒뾽 怨듦컙 猷⑦듃 ?몃????뚯씪 寃쎈줈瑜?嫄곕??섎뒗吏 寃利앺빀?덈떎.
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

    [Fact]
    public async Task CreateDirectoryTool_RejectsPathsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

        var outsideDirectory = Path.Combine(outside.RootPath, "created");
        var tool = new CreateDirectoryTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = outsideDirectory
        });

        Assert.True(result.IsError);
        Assert.False(Directory.Exists(outsideDirectory));
        Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeletePathTool_RejectsPathsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

        var outsideFile = outside.CreateFile("outside.txt", "blocked");
        var tool = new DeletePathTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["path"] = outsideFile
        });

        Assert.True(result.IsError);
        Assert.True(File.Exists(outsideFile));
        Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadFileTool_RejectsSymlinkTargetsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

        var outsideFile = outside.CreateFile("secret.txt", "blocked");
        var linkPath = Path.Combine(workspace.RootPath, "secret-link.txt");
        if (!TryCreateFileSymbolicLink(linkPath, outsideFile))
        {
            return;
        }

        try
        {
            var tool = new ReadFileTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["path"] = "secret-link.txt"
            });

            Assert.True(result.IsError);
            Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFileSystemLink(linkPath);
        }
    }

    [Fact]
    public async Task CreateDirectoryTool_RejectsDirectorySymlinkParentsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

        var linkPath = Path.Combine(workspace.RootPath, "link-out");
        var outsideDirectory = Path.Combine(outside.RootPath, "created");
        if (!TryCreateDirectorySymbolicLink(linkPath, outside.RootPath))
        {
            return;
        }

        try
        {
            var tool = new CreateDirectoryTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["path"] = "link-out/created"
            });

            Assert.True(result.IsError);
            Assert.False(Directory.Exists(outsideDirectory));
            Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFileSystemLink(linkPath);
        }
    }

    [Fact]
    public void ToolPermissionClassifier_TreatsDirectorySymlinkParentWritesAsExternal()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();

        var linkPath = Path.Combine(workspace.RootPath, "link-out");
        if (!TryCreateDirectorySymbolicLink(linkPath, outside.RootPath))
        {
            return;
        }

        try
        {
            var createAssessment = ToolPermissionClassifier.Assess(
                "create_directory",
                new Dictionary<string, object?>
                {
                    ["path"] = "link-out/created"
                },
                workspace.RootPath);
            var writeAssessment = ToolPermissionClassifier.Assess(
                "write_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "link-out/created.txt",
                    ["content"] = "blocked"
                },
                workspace.RootPath);

            Assert.Equal(PermissionRiskLevel.ExternalWrite, createAssessment.RiskLevel);
            Assert.Equal(PermissionRiskLevel.ExternalWrite, writeAssessment.RiskLevel);
        }
        finally
        {
            DeleteFileSystemLink(linkPath);
        }
    }

    [Fact]
    public void ToolPermissionClassifier_TreatsExistingFileThroughDirectorySymlinkAsExternal()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();

        outside.CreateFile("editable.txt", "blocked");
        var linkPath = Path.Combine(workspace.RootPath, "link-out");
        if (!TryCreateDirectorySymbolicLink(linkPath, outside.RootPath))
        {
            return;
        }

        try
        {
            var editAssessment = ToolPermissionClassifier.Assess(
                "edit_file",
                new Dictionary<string, object?>
                {
                    ["path"] = "link-out/editable.txt",
                    ["old_string"] = "blocked",
                    ["new_string"] = "changed"
                },
                workspace.RootPath);
            var deleteAssessment = ToolPermissionClassifier.Assess(
                "delete_path",
                new Dictionary<string, object?>
                {
                    ["path"] = "link-out/editable.txt"
                },
                workspace.RootPath);

            Assert.Equal(PermissionRiskLevel.ExternalWrite, editAssessment.RiskLevel);
            Assert.Equal(PermissionRiskLevel.ExternalWrite, deleteAssessment.RiskLevel);
        }
        finally
        {
            DeleteFileSystemLink(linkPath);
        }
    }

    [Fact]
    public async Task DeletePathTool_RejectsSymlinkTargetsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

        var outsideFile = outside.CreateFile("outside.txt", "blocked");
        var linkPath = Path.Combine(workspace.RootPath, "outside-link.txt");
        if (!TryCreateFileSymbolicLink(linkPath, outsideFile))
        {
            return;
        }

        try
        {
            var tool = new DeletePathTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["path"] = "outside-link.txt"
            });

            Assert.True(result.IsError);
            Assert.True(File.Exists(outsideFile));
            Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFileSystemLink(linkPath);
        }
    }

    [Fact]
    public async Task WriteFileTool_RejectsDirectorySymlinkParentsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

        var linkPath = Path.Combine(workspace.RootPath, "link-out");
        var outsideFile = Path.Combine(outside.RootPath, "created.txt");
        if (!TryCreateDirectorySymbolicLink(linkPath, outside.RootPath))
        {
            return;
        }

        try
        {
            var tool = new WriteFileTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["path"] = "link-out/created.txt",
                ["content"] = "blocked"
            });

            Assert.True(result.IsError);
            Assert.False(File.Exists(outsideFile));
            Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFileSystemLink(linkPath);
        }
    }

    /// <summary>
    /// EditFileTool???묒뾽 怨듦컙 猷⑦듃 ?몃????뚯씪 寃쎈줈瑜?嫄곕??섎뒗吏 寃利앺빀?덈떎.
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

    [Fact]
    public async Task EditFileTool_RejectsSymlinkTargetsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

        var outsideFile = outside.CreateFile("outside.txt", "alpha");
        var linkPath = Path.Combine(workspace.RootPath, "outside-link.txt");
        if (!TryCreateFileSymbolicLink(linkPath, outsideFile))
        {
            return;
        }

        try
        {
            var tool = new EditFileTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["path"] = "outside-link.txt",
                ["old_string"] = "alpha",
                ["new_string"] = "omega"
            });

            Assert.True(result.IsError);
            Assert.Equal("alpha", File.ReadAllText(outsideFile));
            Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFileSystemLink(linkPath);
        }
    }

    [Fact]
    public async Task EditFileTool_RejectsDirectorySymlinkParentsOutsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        SetEnvironment("AGENTQ_WORKSPACE_ROOT", workspace.RootPath);

        var outsideFile = outside.CreateFile("editable.txt", "alpha");
        var linkPath = Path.Combine(workspace.RootPath, "link-out");
        if (!TryCreateDirectorySymbolicLink(linkPath, outside.RootPath))
        {
            return;
        }

        try
        {
            var tool = new EditFileTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["path"] = "link-out/editable.txt",
                ["old_string"] = "alpha",
                ["new_string"] = "omega"
            });

            Assert.True(result.IsError);
            Assert.Equal("alpha", File.ReadAllText(outsideFile));
            Assert.Contains("outside the workspace root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFileSystemLink(linkPath);
        }
    }

    /// <summary>
    /// ?섍꼍 蹂?섎? ?ㅼ젙?섍퀬 ?먮옒 媛믪쓣 異붿쟻?⑸땲??
    /// </summary>
    private void SetEnvironment(string name, string? value)
    {
        if (!_originalEnvironment.ContainsKey(name))
        {
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }

    private static bool TryCreateFileSymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            DeleteFileSystemLink(linkPath);
            return false;
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            DeleteFileSystemLink(linkPath);
            return false;
        }
    }

    private static void DeleteFileSystemLink(string linkPath)
    {
        try
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }
            else if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// ?꾩떆 ?붾젆?좊━? 紐⑤뱺 ?댁슜????젣?⑸땲??
    /// </summary>
    public void Dispose()
    {
        foreach (var pair in _originalEnvironment)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        TryDeleteDirectory(_configHomeRoot);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// ?꾩떆 ?묒뾽 怨듦컙 ?붾젆?좊━瑜?愿由ы븯???ы띁 ?대옒?ㅼ엯?덈떎.
    /// </summary>
    private sealed class TemporaryWorkspace : IDisposable
    {
        /// <summary>
        /// 吏?뺣맂 ?대쫫?쇰줈 ?꾩떆 ?묒뾽 怨듦컙 ?붾젆?좊━瑜??앹꽦?⑸땲??
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
        /// ?꾩떆 ?묒뾽 怨듦컙??猷⑦듃 ?붾젆?좊━ 寃쎈줈?낅땲??
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// 吏?뺣맂 ?곷? 寃쎈줈? ?댁슜?쇰줈 ?뚯씪???앹꽦?⑸땲??
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
        /// ?뚯뒪??以?蹂寃쎈맂 ?섍꼍 蹂?섎? ?먮옒 媛믪쑝濡?蹂듭썝?⑸땲??
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class NamedTestTool(string name, string description, bool requiresPermission = false) : ITool
    {
        public string Name => name;

        public string Description => description;

        public object InputSchema => new
        {
            type = "object",
            properties = new Dictionary<string, object?>()
        };

        public bool RequiresPermission => requiresPermission;

        public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> input, CancellationToken ct = default) =>
            Task.FromResult(ToolResult.Success("{}"));
    }

    private sealed class ThrowingPermissionEnforcer : IPermissionEnforcer
    {
        public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson) =>
            throw new InvalidOperationException("Permission should not be requested before JSON arguments are valid.");
    }

    private sealed class StaticResponseHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
