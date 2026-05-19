using System.IO;
using System.Text;
using System.Text.Json;
using AgentQ.Core.Models;
using AgentQ.Core.Providers;
using AgentQ.Providers.Anthropic;
using AgentQ.Providers.OpenAi;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopAgentService
{
    private const string SystemPrompt =
        """
        You are AgentQ Desktop, a Windows desktop coding assistant.
        Answer in Korean by default unless the user asks for another language.
        Assume the user is working on Windows. Prefer safe, concise guidance.
        You can use tools to read files, search the workspace, edit files, write files, and run shell commands.
        Prefer inspecting files before editing. After making code changes, run focused build or test commands when useful.
        For coding tasks, work in a loop: plan briefly, gather context, act with tools, observe results, repair failures, then verify.
        Treat build, test, and command failures as diagnostic input. Fix what you can before asking the user to intervene.
        Keep tool use scoped to the selected workspace and explain important changes clearly.
        """;

    private const int MaxToolSteps = 45;
    private const int MaxToolResultChars = 24000;
    private const int MaxChangeSnapshotChars = 160000;

    private readonly List<ChatMessage> _messages = [];
    private readonly LinkContentFetcher _linkContentFetcher = new();
    private readonly ProjectMemoryService _projectMemoryService = new();
    private readonly WorkspaceIndexer _workspaceIndexer = new();
    private readonly ToolRegistry _toolRegistry = CreateToolRegistry();

    public async Task<string> SendAsync(
        ProviderConfiguration config,
        string userText,
        IReadOnlyList<DesktopAttachment>? attachments = null,
        string? workspaceRoot = null,
        AgentWorkMode workMode = AgentWorkMode.Coding,
        Action<string>? onDelta = null,
        IPermissionEnforcer? permissionEnforcer = null,
        DesktopToolCallbacks? toolCallbacks = null,
        CancellationToken ct = default)
    {
        toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Planning, "Run started", "Preparing provider and workspace context.");
        var provider = CreateProvider(config);
        var effectiveWorkspaceRoot = ResolveWorkspaceRoot(workspaceRoot);
        toolCallbacks?.OnRunStep?.Invoke(AgentRunState.GatheringContext, "Gathering context", effectiveWorkspaceRoot);
        var projectMemory = await _projectMemoryService.LoadOrDiscoverAsync(effectiveWorkspaceRoot, ct);
        var transientContext = await BuildContextOnlyAsync(config, userText, effectiveWorkspaceRoot, projectMemory, ct);
        _messages.Add(await CreateUserMessageAsync(userText, attachments ?? [], ct));
        var builder = new StringBuilder();
        var enforcer = permissionEnforcer ?? new DenyByDefaultPermissionEnforcer();
        var includeTransientContext = !string.IsNullOrWhiteSpace(transientContext);
        var fileChanges = new List<FileChangeRecord>();
        var executedCommands = new List<string>();

        for (var step = 1; step <= MaxToolSteps; step++)
        {
            toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Generating, $"Model turn {step}", "Waiting for assistant output or tool calls.");
            var response = await GenerateAssistantTurnAsync(
                provider,
                config,
                includeTransientContext ? transientContext : null,
                builder,
                onDelta,
                toolCallbacks?.OnUsage,
                ct);
            includeTransientContext = false;
            if (response.AssistantContent.Count > 0)
            {
                _messages.Add(new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = response.AssistantContent
                });
            }

            if (response.ToolUses.Count == 0)
            {
                ReportVerificationPlans(fileChanges, executedCommands, projectMemory, toolCallbacks);
                toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Done, "Run complete", "Assistant finished without more tool calls.");
                return builder.ToString();
            }

            toolCallbacks?.OnRunStep?.Invoke(AgentRunState.RunningTool, $"Executing {response.ToolUses.Count} tool call(s)", null);
            var toolResults = await ExecuteToolsAsync(
                response.ToolUses,
                enforcer,
                toolCallbacks,
                effectiveWorkspaceRoot,
                workMode,
                fileChanges,
                executedCommands,
                ct);
            if (toolResults.Count > 0)
            {
                _messages.Add(new ChatMessage
                {
                    Role = ChatRole.User,
                    Content = toolResults
                });
            }
        }

        var stoppedMessage = $"Stopped after reaching the maximum tool steps ({MaxToolSteps}).";
        builder.AppendLine();
        builder.AppendLine(stoppedMessage);
        onDelta?.Invoke(Environment.NewLine + stoppedMessage);
        _messages.Add(ChatMessage.AssistantText(stoppedMessage));
        ReportVerificationPlans(fileChanges, executedCommands, projectMemory, toolCallbacks);
        toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Failed, "Tool step limit reached", stoppedMessage);
        return builder.ToString();
    }

    private async Task<DesktopAssistantTurn> GenerateAssistantTurnAsync(
        ILlmProvider provider,
        ProviderConfiguration config,
        string? transientContext,
        StringBuilder textBuilder,
        Action<string>? onDelta,
        Action<UsageStats>? onUsage,
        CancellationToken ct)
    {
        var requestMessages = BuildRequestMessages(transientContext);
        var context = new ChatContext
        {
            Model = config.Model,
            SystemPrompt = SystemPrompt,
            Messages = requestMessages,
            MaxTokens = config.MaxTokens == 0 ? 4096 : config.MaxTokens,
            Stream = true,
            MaxSteps = MaxToolSteps
        };

        var assistantText = new StringBuilder();
        var reasoningContent = new StringBuilder();
        var toolUses = new List<ChatContent>();
        var tools = _toolRegistry.GetToolDefinitions().Select(tool => new ToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.InputSchema
        });

        await foreach (var chunk in provider.GenerateStreamAsync(context, tools, ct))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                assistantText.Append(chunk.TextDelta);
                textBuilder.Append(chunk.TextDelta);
                onDelta?.Invoke(chunk.TextDelta);
            }

            if (!string.IsNullOrEmpty(chunk.ReasoningDelta))
            {
                reasoningContent.Append(chunk.ReasoningDelta);
            }

            if (chunk.Usage != null)
            {
                onUsage?.Invoke(chunk.Usage);
            }

            if (chunk.ToolUseDelta?.IsComplete == true)
            {
                toolUses.Add(ChatContent.CreateToolUse(
                    chunk.ToolUseDelta.ToolId,
                    chunk.ToolUseDelta.ToolName,
                    chunk.ToolUseDelta.PartialInput ?? "{}"));
            }
        }

        if (reasoningContent.Length > 0)
        {
            foreach (var toolUse in toolUses)
            {
                toolUse.ReasoningContent = reasoningContent.ToString();
            }
        }

        var assistantContent = new List<ChatContent>();
        if (assistantText.Length > 0)
        {
            assistantContent.Add(ChatContent.CreateText(assistantText.ToString()));
        }

        assistantContent.AddRange(toolUses);
        return new DesktopAssistantTurn(assistantContent, toolUses);
    }

    public void ClearConversation()
    {
        _messages.Clear();
    }

    private List<ChatMessage> BuildRequestMessages(string? transientContext)
    {
        var messages = _messages.ToList();
        if (string.IsNullOrWhiteSpace(transientContext))
        {
            return messages;
        }

        var insertIndex = Math.Max(0, messages.Count - 1);
        messages.Insert(insertIndex, ChatMessage.UserText(transientContext));
        return messages;
    }

    private async Task<string> BuildContextOnlyAsync(
        ProviderConfiguration config,
        string userText,
        string workspaceRoot,
        ProjectMemory projectMemory,
        CancellationToken ct)
    {
        var workspaceContext = config.DesktopAutoAttachWorkspaceContext
            ? await _workspaceIndexer.BuildContextAsync(workspaceRoot, ct)
            : string.Empty;
        var linkedContext = config.DesktopAutoFetchLinks
            ? await _linkContentFetcher.BuildContextAsync(userText, ct)
            : string.Empty;
        var memoryContext = _projectMemoryService.BuildContext(projectMemory);

        if (string.IsNullOrWhiteSpace(workspaceContext) &&
            string.IsNullOrWhiteSpace(linkedContext) &&
            string.IsNullOrWhiteSpace(memoryContext))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("The desktop app attached local context for this request only.");
        builder.AppendLine("This context is not part of the saved conversation history.");
        builder.AppendLine("Use the workspace snapshot for repository questions, but say when a file may be missing from the snapshot.");
        builder.AppendLine($"Current AgentQ work mode: {config.DesktopWorkMode}.");

        if (!string.IsNullOrWhiteSpace(workspaceContext))
        {
            builder.AppendLine();
            builder.AppendLine(workspaceContext);
        }

        if (!string.IsNullOrWhiteSpace(memoryContext))
        {
            builder.AppendLine();
            builder.AppendLine(memoryContext);
        }

        if (!string.IsNullOrWhiteSpace(linkedContext))
        {
            builder.AppendLine();
            builder.AppendLine(linkedContext);
        }

        return builder.ToString().TrimEnd();
    }

    private static async Task<ChatMessage> CreateUserMessageAsync(
        string userText,
        IReadOnlyList<DesktopAttachment> attachments,
        CancellationToken ct)
    {
        var content = new List<ChatContent> { ChatContent.CreateText(userText) };
        var videoNotes = new List<string>();

        foreach (var attachment in attachments)
        {
            if (attachment.IsImage)
            {
                var bytes = await File.ReadAllBytesAsync(attachment.Path, ct);
                content.Add(ChatContent.CreateImage(attachment.MediaType, Convert.ToBase64String(bytes)));
            }
            else if (attachment.IsVideo)
            {
                var result = await VideoFrameExtractor.ExtractFramesAsync(attachment.Path, ct);
                if (!result.IsAvailable)
                {
                    videoNotes.Add($"{attachment.FileName}: ffmpeg를 찾지 못해 프레임을 추출하지 못했습니다.");
                    continue;
                }

                if (result.FramePaths.Count == 0)
                {
                    videoNotes.Add($"{attachment.FileName}: 분석할 프레임을 추출하지 못했습니다.");
                    continue;
                }

                videoNotes.Add($"{attachment.FileName}: 동영상에서 대표 프레임 {result.FramePaths.Count}장을 추출해 이미지로 분석합니다.");
                try
                {
                    foreach (var framePath in result.FramePaths)
                    {
                        var bytes = await File.ReadAllBytesAsync(framePath, ct);
                        content.Add(ChatContent.CreateImage("image/jpeg", Convert.ToBase64String(bytes)));
                    }
                }
                finally
                {
                    TryDeleteFrameDirectory(result.FramePaths);
                }
            }
        }

        if (videoNotes.Count > 0)
        {
            content.Add(ChatContent.CreateText(string.Join(Environment.NewLine, videoNotes)));
        }

        return new ChatMessage
        {
            Role = ChatRole.User,
            Content = content
        };
    }

    private static void TryDeleteFrameDirectory(IReadOnlyList<string> framePaths)
    {
        var firstFrame = framePaths.FirstOrDefault();
        if (firstFrame == null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(firstFrame);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Temporary frame cleanup is best-effort.
        }
    }

    private static ILlmProvider CreateProvider(ProviderConfiguration config)
    {
        ILlmProvider provider = config.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAiCompatibleProvider(config.BaseUrl, config.ApiKey),
            "opencode-go" => new OpenAiCompatibleProvider(config.BaseUrl, config.ApiKey, name: "opencode-go"),
            "anthropic" => new AnthropicProvider(config.BaseUrl, config.ApiKey),
            _ => new OpenAiCompatibleProvider(config.BaseUrl, config.ApiKey, name: config.Provider)
        };

        return new ResilientLlmProvider(provider);
    }

    private async Task<List<ChatContent>> ExecuteToolsAsync(
        IReadOnlyList<ChatContent> toolUses,
        IPermissionEnforcer enforcer,
        DesktopToolCallbacks? callbacks,
        string workspaceRoot,
        AgentWorkMode workMode,
        List<FileChangeRecord> fileChanges,
        List<string> executedCommands,
        CancellationToken ct)
    {
        var results = new List<ChatContent>();

        using (new WorkspaceRootEnvironmentScope(workspaceRoot))
        {
            foreach (var toolUse in toolUses)
            {
                var toolName = toolUse.ToolName ?? string.Empty;
                var toolId = toolUse.ToolId ?? string.Empty;
                var tool = _toolRegistry.Get(toolName);
                if (tool == null)
                {
                    callbacks?.OnToolError?.Invoke(toolName, $"Tool not found: {toolName}");
                    results.Add(ChatContent.CreateToolResult(toolId, $"Tool not found: {toolName}", true));
                    continue;
                }

                var parsedInput = DesktopToolInputParser.Parse(toolUse.ToolInput);
                TrackExecutedCommand(tool.Name, parsedInput, executedCommands);
                var inputJson = JsonSerializer.Serialize(parsedInput, new JsonSerializerOptions { WriteIndented = true });
                if (tool.RequiresPermission &&
                    !await RequestToolPermissionAsync(tool, inputJson, workMode, enforcer, callbacks))
                {
                    callbacks?.OnPermissionDenied?.Invoke(tool.Name);
                    results.Add(ChatContent.CreateToolResult(toolId, "Permission denied by user", true));
                    continue;
                }

                callbacks?.OnToolExecution?.Invoke(tool.Name);

                try
                {
                    var snapshot = await CaptureFileSnapshotAsync(tool.Name, parsedInput, workspaceRoot, ct);
                    var result = await tool.ExecuteAsync(parsedInput, ct);
                    if (result.IsError)
                    {
                        callbacks?.OnToolError?.Invoke(tool.Name, result.Content);
                    }
                    else
                    {
                        callbacks?.OnToolOutput?.Invoke(tool.Name, result.Content);
                    }

                    if (!result.IsError)
                    {
                        var change = await BuildFileChangeRecordAsync(snapshot, workspaceRoot, ct);
                        if (change != null)
                        {
                            fileChanges.Add(change);
                            callbacks?.OnRunStep?.Invoke(AgentRunState.RecordingChanges, "Recorded file change", change.RelativePath);
                            callbacks?.OnFileChanged?.Invoke(change);
                        }
                    }

                    results.Add(ChatContent.CreateToolResult(
                        toolId,
                        TruncateToolResult(result.Content, out var wasTruncated),
                        result.IsError));

                    if (wasTruncated)
                    {
                        callbacks?.OnToolOutput?.Invoke(tool.Name, $"Tool result was truncated to {MaxToolResultChars} chars before being sent back to the model.");
                    }
                }
                catch (Exception ex)
                {
                    var message = $"Error: {ex.Message}";
                    callbacks?.OnRunStep?.Invoke(AgentRunState.Failed, $"Tool failed: {tool.Name}", message);
                    callbacks?.OnToolError?.Invoke(tool.Name, message);
                    results.Add(ChatContent.CreateToolResult(toolId, message, true));
                }
            }
        }

        return results;
    }

    private static void ReportVerificationPlans(
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<string> executedCommands,
        ProjectMemory projectMemory,
        DesktopToolCallbacks? callbacks)
    {
        foreach (var plan in DesktopVerificationSelector.SelectPlans(fileChanges, executedCommands, projectMemory))
        {
            callbacks?.OnVerificationPlan?.Invoke(plan);
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Verifying,
                plan.Title,
                plan.AlreadySatisfied ? plan.Reason : plan.Detail);
        }
    }

    private static void TrackExecutedCommand(
        string toolName,
        IReadOnlyDictionary<string, object?> input,
        List<string> executedCommands)
    {
        if (!string.Equals(toolName, "bash", StringComparison.Ordinal) ||
            !input.TryGetValue("command", out var commandValue) ||
            commandValue is not string command ||
            string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        executedCommands.Add(command);
    }

    private static async Task<bool> RequestToolPermissionAsync(
        ITool tool,
        string inputJson,
        AgentWorkMode workMode,
        IPermissionEnforcer enforcer,
        DesktopToolCallbacks? callbacks)
    {
        var policy = ToolPermissionPolicy.Evaluate(tool.Name, inputJson, workMode);
        var assessment = policy.Assessment;
        if (policy.IsBlocked)
        {
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Failed,
                $"Blocked: {assessment.RiskLevel} ({workMode})",
                $"{assessment.Summary}{Environment.NewLine}{policy.PolicyReason}");
            return false;
        }

        if (policy.Decision == ToolPermissionDecision.Allow)
        {
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.RunningTool,
                $"Allowed: {assessment.RiskLevel} ({workMode})",
                assessment.Summary);
            return true;
        }

        callbacks?.OnRunStep?.Invoke(
            AgentRunState.WaitingForApproval,
            $"Approval needed: {assessment.RiskLevel} ({workMode})",
            $"{assessment.Summary}{Environment.NewLine}{policy.PolicyReason}");
        var allowed = await enforcer.RequestPermissionAsync(tool.Name, tool.Description, inputJson);
        callbacks?.OnRunStep?.Invoke(
            allowed ? AgentRunState.RunningTool : AgentRunState.Failed,
            allowed ? $"Approved: {assessment.RiskLevel} ({workMode})" : $"Denied: {assessment.RiskLevel} ({workMode})",
            allowed ? assessment.Summary : $"{assessment.Reason}{Environment.NewLine}{policy.PolicyReason}");
        return allowed;
    }

    private static async Task<FileSnapshot?> CaptureFileSnapshotAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> input,
        string workspaceRoot,
        CancellationToken ct)
    {
        if (!IsFileMutationTool(toolName) ||
            !input.TryGetValue("path", out var pathValue) ||
            pathValue is not string path)
        {
            return null;
        }

        if (!TryResolveWorkspaceFile(path, workspaceRoot, out var fullPath))
        {
            return null;
        }

        var before = await ReadSnapshotTextAsync(fullPath, ct);
        return new FileSnapshot(fullPath, before);
    }

    private static async Task<FileChangeRecord?> BuildFileChangeRecordAsync(
        FileSnapshot? snapshot,
        string workspaceRoot,
        CancellationToken ct)
    {
        if (snapshot == null)
        {
            return null;
        }

        var after = await ReadSnapshotTextAsync(snapshot.FullPath, ct);
        if (string.Equals(snapshot.Before, after, StringComparison.Ordinal))
        {
            return null;
        }

        return new FileChangeRecord
        {
            Path = snapshot.FullPath,
            RelativePath = Path.GetRelativePath(workspaceRoot, snapshot.FullPath).Replace('\\', '/'),
            Before = snapshot.Before,
            After = after,
            DiffLines = LineDiffBuilder.Build(snapshot.Before, after)
        };
    }

    private static bool IsFileMutationTool(string toolName)
    {
        return toolName is "write_file" or "edit_file";
    }

    private static bool TryResolveWorkspaceFile(string path, string workspaceRoot, out string fullPath)
    {
        fullPath = string.Empty;
        try
        {
            var root = Path.GetFullPath(workspaceRoot);
            fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(root, path));

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            return fullPath.Equals(root, comparison) ||
                   fullPath.StartsWith(rootWithSeparator, comparison);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ReadSnapshotTextAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path) || Directory.Exists(path))
            {
                return string.Empty;
            }

            var text = await File.ReadAllTextAsync(path, ct);
            return text.Length <= MaxChangeSnapshotChars
                ? text
                : text[..MaxChangeSnapshotChars] + Environment.NewLine + "[snapshot truncated]";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveWorkspaceRoot(string? workspaceRoot)
    {
        return string.IsNullOrWhiteSpace(workspaceRoot)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workspaceRoot);
    }

    private static string TruncateToolResult(string value, out bool wasTruncated)
    {
        if (value.Length <= MaxToolResultChars)
        {
            wasTruncated = false;
            return value;
        }

        wasTruncated = true;
        return value[..MaxToolResultChars] + Environment.NewLine + "[tool result truncated]";
    }

    private static ToolRegistry CreateToolRegistry()
    {
        var registry = new ToolRegistry();
        registry.Register(new BashTool());
        registry.Register(new ReadFileTool());
        registry.Register(new WriteFileTool());
        registry.Register(new EditFileTool());
        registry.Register(new GrepTool());
        registry.Register(new GlobTool());
        registry.Register(new PluginEchoTool());
        return registry;
    }
}

internal sealed record DesktopAssistantTurn(
    List<ChatContent> AssistantContent,
    List<ChatContent> ToolUses);

internal sealed class DenyByDefaultPermissionEnforcer : IPermissionEnforcer
{
    public Task<bool> RequestPermissionAsync(string toolName, string description, string inputJson) =>
        Task.FromResult(false);
}

internal sealed class WorkspaceRootEnvironmentScope : IDisposable
{
    private readonly string? _previousWorkspaceRoot;
    private readonly string? _previousClawWorkspaceRoot;

    public WorkspaceRootEnvironmentScope(string workspaceRoot)
    {
        _previousWorkspaceRoot = Environment.GetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT");
        _previousClawWorkspaceRoot = Environment.GetEnvironmentVariable("CLAW_WORKSPACE_ROOT");
        Environment.SetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT", workspaceRoot);
        Environment.SetEnvironmentVariable("CLAW_WORKSPACE_ROOT", workspaceRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT", _previousWorkspaceRoot);
        Environment.SetEnvironmentVariable("CLAW_WORKSPACE_ROOT", _previousClawWorkspaceRoot);
    }
}

internal sealed record FileSnapshot(string FullPath, string Before);
