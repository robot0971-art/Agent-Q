using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
        AgentQ was developed by robot0971-art.
        You are not Kimi, Moonshot AI, OpenAI, Anthropic, DeepSeek, or any model provider.
        Model providers are only the underlying inference engines used by AgentQ.
        If asked who developed AgentQ or who made you, answer that AgentQ was developed by robot0971-art.
        If asked about the underlying model, mention the selected provider or model separately.
        Answer in Korean by default unless the user asks for another language.
        Assume the user is working on Windows. Prefer safe, concise guidance.
        You can use tools to read files, search the workspace, edit files, write files, and run shell commands.
        Prefer inspecting files before editing. After making code changes, run focused build or test commands when useful.
        Prefer hybrid_search for codebase discovery because it combines symbol, semantic, keyword, and project-map evidence.
        For code navigation, prefer symbol_search first when the user mentions a function, class, component, method, or likely identifier.
        Use semantic_search when embeddings are available and the request is meaning-based; use grep_search/glob_search for broad text or file pattern fallback.
        After symbol_search or search results identify candidate files, read the most relevant files before editing.
        For coding tasks, work in a loop: plan briefly, gather context, act with tools, observe results, repair failures, then verify.
        Treat build, test, and command failures as diagnostic input. Fix what you can before asking the user to intervene.
        Keep tool use scoped to the selected workspace and explain important changes clearly.
        For project analysis, documentation, architecture summaries, and reviews, include the main evidence you inspected.
        Separate confirmed facts from assumptions or items that still need verification.
        Do not invent exact dependencies, package names, indexing strategies, release state, or implementation details when you have not inspected supporting files or command output.
        AgentQ Desktop can attempt to read HTTP/HTTPS links when link auto-read is enabled.
        Never answer that AgentQ categorically cannot access external websites; describe the current link auto-read setting, fetch result, or fallback instead.
        If the user asks whether links can be read without providing a URL, ask them to send the URL.
        If link auto-read is enabled and linked page context is attached, do not claim that AgentQ cannot access external URLs.
        For URL questions, use the linked page context when fetch succeeded; when it failed, report the fetch failure reason and suggest pasted text or a local file as fallback.
        """;

    private const int DefaultMaxToolSteps = 50;
    private const int ReadonlyMaxToolSteps = 20;
    private const int MaxConfiguredToolSteps = 100;
    private const int MaxToolResultChars = 24000;
    private const int MaxChangeSnapshotChars = 160000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LinkContentFetcher _linkContentFetcher;
    private readonly ProjectMemoryService _projectMemoryService;
    private readonly WorkspaceIndexer _workspaceIndexer;
    private readonly EmbeddingIndexStore _embeddingIndexStore;
    private readonly DesktopEmbeddingClientFactory _embeddingClientFactory;
    private readonly FileMutationSnapshotService _fileMutationSnapshotService;
    private readonly ToolReplayService _toolReplayService;
    private readonly WorkspaceSymbolIndexService _symbolIndexService;
    private readonly WorkspaceAnalysisService _workspaceAnalysisService;
    private readonly List<ChatMessage> _messages = [];

    public DesktopAgentService(
        IHttpClientFactory httpClientFactory,
        LinkContentFetcher linkContentFetcher,
        ProjectMemoryService projectMemoryService,
        WorkspaceIndexer workspaceIndexer,
        EmbeddingIndexStore embeddingIndexStore,
        DesktopEmbeddingClientFactory embeddingClientFactory,
        FileMutationSnapshotService fileMutationSnapshotService,
        ToolReplayService toolReplayService,
        WorkspaceSymbolIndexService symbolIndexService,
        WorkspaceAnalysisService workspaceAnalysisService)
    {
        _httpClientFactory = httpClientFactory;
        _linkContentFetcher = linkContentFetcher;
        _projectMemoryService = projectMemoryService;
        _workspaceIndexer = workspaceIndexer;
        _embeddingIndexStore = embeddingIndexStore;
        _embeddingClientFactory = embeddingClientFactory;
        _fileMutationSnapshotService = fileMutationSnapshotService;
        _toolReplayService = toolReplayService;
        _symbolIndexService = symbolIndexService;
        _workspaceAnalysisService = workspaceAnalysisService;
    }

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
        var projectConfig = ProjectAgentConfigService.LoadLocal(effectiveWorkspaceRoot);
        var taskProfile = DesktopPromptAssemblyService.BuildTaskProfile(userText);
        toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Planning, "Task profile", taskProfile.Label);
        var routingRecommendation = DesktopModelRoutingAdvisor.Recommend(userText, taskProfile, config, workMode);
        toolCallbacks?.OnRunStep?.Invoke(
            AgentRunState.Planning,
            $"Model route: {routingRecommendation.Label}",
            routingRecommendation.CurrentModelMatches
                ? $"Current model matches route. {routingRecommendation.DisplayText}"
                : $"Suggested route differs from current model. {routingRecommendation.DisplayText}");
        var transientContext = await BuildContextOnlyAsync(config, userText, effectiveWorkspaceRoot, projectMemory, projectConfig, taskProfile, ct);
        var touchedLessons = await _projectMemoryService.TouchRelevantLocalLessonsAsync(effectiveWorkspaceRoot, userText, ct);
        if (touchedLessons.Count > 0)
        {
            var errorHistoryLessons = touchedLessons
                .Where(lesson => lesson.Tags.Any(tag => tag.Equals("error-history", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (errorHistoryLessons.Count > 0)
            {
                toolCallbacks?.OnRunStep?.Invoke(
                    AgentRunState.GatheringContext,
                    "Evidence: previous failure memory",
                    string.Join(", ", errorHistoryLessons.Select(lesson => string.IsNullOrWhiteSpace(lesson.Title) ? lesson.Id : lesson.Title)));
            }

            toolCallbacks?.OnRunStep?.Invoke(
                AgentRunState.GatheringContext,
                "Evidence: project memory",
                string.Join(", ", touchedLessons.Select(lesson => string.IsNullOrWhiteSpace(lesson.Title) ? lesson.Id : lesson.Title)));
        }

        _messages.Add(await CreateUserMessageAsync(userText, attachments ?? [], ct));
        var builder = new StringBuilder();
        var enforcer = permissionEnforcer ?? new DenyByDefaultPermissionEnforcer();
        var includeTransientContext = !string.IsNullOrWhiteSpace(transientContext);
        var fileChanges = new List<FileChangeRecord>();
        var executedCommands = new List<string>();
        var replayEntries = new List<ToolReplayEntry>();
        var executedToolCount = 0;
        var toolRegistry = CreateToolRegistry(config, effectiveWorkspaceRoot);

        var maxToolSteps = ResolveMaxToolSteps(config, workMode);

        for (var step = 1; step <= maxToolSteps; step++)
        {
            toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Generating, $"Model turn {step}", "Waiting for assistant output or tool calls.");
            var response = await GenerateAssistantTurnAsync(
                provider,
                config,
                toolRegistry,
                maxToolSteps,
                taskProfile,
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
                var verificationPlans = ReportVerificationPlans(fileChanges, executedCommands, projectMemory, toolCallbacks);
                ReportConfidence(
                    builder.ToString(),
                    executedToolCount,
                    fileChanges,
                    executedCommands,
                    verificationPlans,
                    touchedLessons.Count,
                    toolCallbacks);
                toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Done, "Run complete", "Assistant finished without more tool calls.");
                await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
                return builder.ToString();
            }

            executedToolCount += response.ToolUses.Count;
            toolCallbacks?.OnRunStep?.Invoke(AgentRunState.RunningTool, $"Executing {response.ToolUses.Count} tool call(s)", null);
            var toolResults = await ExecuteToolsAsync(
                response.ToolUses,
                toolRegistry,
                enforcer,
                toolCallbacks,
                effectiveWorkspaceRoot,
                workMode,
                fileChanges,
                executedCommands,
                replayEntries,
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

        var stoppedMessage = $"Stopped after reaching the maximum tool steps ({maxToolSteps}).";
        builder.AppendLine();
        builder.AppendLine(stoppedMessage);
        onDelta?.Invoke(Environment.NewLine + stoppedMessage);
        _messages.Add(ChatMessage.AssistantText(stoppedMessage));
        var stoppedVerificationPlans = ReportVerificationPlans(fileChanges, executedCommands, projectMemory, toolCallbacks);
        ReportConfidence(
            builder.ToString(),
            executedToolCount,
            fileChanges,
            executedCommands,
            stoppedVerificationPlans,
            touchedLessons.Count,
            toolCallbacks);
        toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Failed, "Tool step limit reached", stoppedMessage);
        await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
        return builder.ToString();
    }

    private async Task<DesktopAssistantTurn> GenerateAssistantTurnAsync(
        ILlmProvider provider,
        ProviderConfiguration config,
        ToolRegistry toolRegistry,
        int maxToolSteps,
        DesktopTaskProfile taskProfile,
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
            SystemPrompt = DesktopPromptAssemblyService.BuildSystemPrompt(SystemPrompt, taskProfile),
            Messages = requestMessages,
            MaxTokens = config.MaxTokens == 0 ? 4096 : config.MaxTokens,
            Stream = true,
            MaxSteps = maxToolSteps
        };

        var assistantText = new StringBuilder();
        var reasoningContent = new StringBuilder();
        var toolUses = new List<ChatContent>();
        var tools = toolRegistry.GetToolDefinitions().Select(tool => new ToolDefinition
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
        ProjectAgentConfig? projectConfig,
        DesktopTaskProfile taskProfile,
        CancellationToken ct)
    {
        var workspaceContext = config.DesktopAutoAttachWorkspaceContext
            ? await _workspaceIndexer.BuildContextAsync(workspaceRoot, ct)
            : string.Empty;
        var linkedContext = config.DesktopAutoFetchLinks
            ? await _linkContentFetcher.BuildContextAsync(userText, ct)
            : string.Empty;
        var memoryContext = _projectMemoryService.BuildContext(projectMemory, userText);
        var mcpContext = McpServerRegistry.BuildContext(projectConfig);
        var hasLinkIntent = HasLinkIntent(userText);
        var linkStatusContext = BuildLinkStatusContext(config, userText, linkedContext, hasLinkIntent);

        if (string.IsNullOrWhiteSpace(workspaceContext) &&
            string.IsNullOrWhiteSpace(linkedContext) &&
            string.IsNullOrWhiteSpace(memoryContext) &&
            string.IsNullOrWhiteSpace(mcpContext) &&
            string.IsNullOrWhiteSpace(linkStatusContext))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("The desktop app attached local context for this request only.");
        builder.AppendLine("This context is not part of the saved conversation history.");
        builder.AppendLine("Use the workspace snapshot for repository questions, but say when a file may be missing from the snapshot.");
        builder.AppendLine($"Current AgentQ work mode: {config.DesktopWorkMode}.");
        builder.AppendLine($"Current task profile: {taskProfile.Label}.");
        builder.AppendLine(taskProfile.ContextHint);
        builder.AppendLine("Codebase discovery hint: use hybrid_search first when you need ranked candidate files with reasons.");
        builder.AppendLine("Code navigation hint: use symbol_search for known or likely identifiers before broad grep; then read_file the best candidate.");
        builder.AppendLine("Search fallback order: symbol_search for definitions, semantic_search for meaning-based context when enabled, grep_search/glob_search for broad fallback.");
        builder.AppendLine("Evidence-backed analysis rule: when answering project analysis or documentation questions, cite the inspected files or commands in a short Evidence section and put unsupported inferences under Needs verification.");
        builder.AppendLine("Link capability rule: AgentQ Desktop can attempt to fetch HTTP/HTTPS URLs when link auto-read is enabled. Never say AgentQ cannot access URLs categorically.");

        if (!string.IsNullOrWhiteSpace(linkStatusContext))
        {
            builder.AppendLine(linkStatusContext);
        }

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

        if (!string.IsNullOrWhiteSpace(mcpContext))
        {
            builder.AppendLine();
            builder.AppendLine(mcpContext);
        }

        if (!string.IsNullOrWhiteSpace(linkedContext))
        {
            builder.AppendLine();
            builder.AppendLine(linkedContext);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildLinkStatusContext(
        ProviderConfiguration config,
        string userText,
        string linkedContext,
        bool hasLinkIntent)
    {
        var containsUrl = LinkContentFetcher.ContainsUrl(userText);
        if (!hasLinkIntent && !containsUrl && string.IsNullOrWhiteSpace(linkedContext))
        {
            return string.Empty;
        }

        if (config.DesktopAutoFetchLinks)
        {
            if (!string.IsNullOrWhiteSpace(linkedContext))
            {
                return "Link auto-read status: enabled. Linked page context is attached below; use it as evidence and report fetch success or failure.";
            }

            return containsUrl
                ? "Link auto-read status: enabled, but no linked page context was attached. Report that no readable linked context was available and ask for pasted text or a local file as fallback."
                : "Link auto-read status: enabled, but no HTTP/HTTPS URL was detected in the current user message. Ask the user to send the URL.";
        }

        return "Link auto-read status: disabled in the current run configuration. Tell the user link auto-read is disabled in settings, and ask them to enable it, paste the content, or attach a local file.";
    }

    private static bool HasLinkIntent(string text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               (text.Contains("link", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("url", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("website", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("web site", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("링크", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("사이트", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("웹사이트", StringComparison.OrdinalIgnoreCase));
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

                videoNotes.Add($"{attachment.FileName}: 동영상에서 대표 프레임 {result.FramePaths.Count}개를 추출해 이미지로 분석합니다.");
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

    private ILlmProvider CreateProvider(ProviderConfiguration config)
    {
        ILlmProvider provider = config.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey)),
            "opencode-go" => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), name: "opencode-go"),
            "anthropic" => new AnthropicProvider(CreateAnthropicClient(config.BaseUrl), config.ApiKey),
            _ => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), name: config.Provider)
        };

        return new ResilientLlmProvider(provider);
    }

    private HttpClient CreateAnthropicClient(string baseUrl)
    {
        var client = _httpClientFactory.CreateClient("anthropic");
        client.BaseAddress = new Uri(baseUrl);
        return client;
    }

    private HttpClient CreateOpenAiClient(string baseUrl, string apiKey)
    {
        var client = _httpClientFactory.CreateClient("openai");
        client.BaseAddress = new Uri(OpenAiCompatibleProvider.NormalizeBaseUrl(baseUrl));
        if (!string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return client;
    }

    private static int ResolveMaxToolSteps(ProviderConfiguration config, AgentWorkMode workMode)
    {
        if (config.DesktopMaxToolSteps > 0)
        {
            return Math.Clamp(config.DesktopMaxToolSteps, 1, MaxConfiguredToolSteps);
        }

        return workMode switch
        {
            AgentWorkMode.Readonly => ReadonlyMaxToolSteps,
            AgentWorkMode.Coding => DefaultMaxToolSteps,
            AgentWorkMode.FullAgent => DefaultMaxToolSteps,
            _ => DefaultMaxToolSteps
        };
    }

    private async Task<List<ChatContent>> ExecuteToolsAsync(
        IReadOnlyList<ChatContent> toolUses,
        ToolRegistry toolRegistry,
        IPermissionEnforcer enforcer,
        DesktopToolCallbacks? callbacks,
        string workspaceRoot,
        AgentWorkMode workMode,
        List<FileChangeRecord> fileChanges,
        List<string> executedCommands,
        List<ToolReplayEntry> replayEntries,
        CancellationToken ct)
    {
        var results = new List<ChatContent>();

        using (new WorkspaceRootEnvironmentScope(workspaceRoot))
        {
            foreach (var toolUse in toolUses)
            {
                var toolName = toolUse.ToolName ?? string.Empty;
                var toolId = toolUse.ToolId ?? string.Empty;
                var tool = toolRegistry.Get(toolName);
                if (tool == null)
                {
                    callbacks?.OnToolError?.Invoke(toolName, $"Tool not found: {toolName}");
                    replayEntries.Add(CreateReplayEntry(toolName, toolId, "{}", $"Tool not found: {toolName}", isError: true, DateTime.UtcNow));
                    results.Add(ChatContent.CreateToolResult(toolId, $"Tool not found: {toolName}", true));
                    continue;
                }

                var parsedInput = DesktopToolInputParser.Parse(toolUse.ToolInput);
                TrackExecutedCommand(tool.Name, parsedInput, executedCommands);
                var inputJson = JsonSerializer.Serialize(parsedInput, new JsonSerializerOptions { WriteIndented = true });
                var evidence = DesktopEvidenceFormatter.DescribeToolEvidence(tool.Name, parsedInput, workspaceRoot);
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    callbacks?.OnRunStep?.Invoke(AgentRunState.RunningTool, $"Evidence: {tool.Name}", evidence);
                }

                if (tool.RequiresPermission &&
                    !await RequestToolPermissionAsync(tool, inputJson, workMode, enforcer, callbacks))
                {
                    callbacks?.OnPermissionDenied?.Invoke(tool.Name);
                    replayEntries.Add(CreateReplayEntry(tool.Name, toolId, inputJson, "Permission denied by user", isError: true, DateTime.UtcNow));
                    results.Add(ChatContent.CreateToolResult(toolId, "Permission denied by user", true));
                    continue;
                }

                callbacks?.OnToolExecution?.Invoke(tool.Name);
                var startedAt = DateTime.UtcNow;

                try
                {
                    var snapshot = await CaptureFileSnapshotAsync(tool.Name, parsedInput, workspaceRoot, ct);
                    var result = await tool.ExecuteAsync(parsedInput, ct);
                    result = await DesktopSearchRetryService.ApplySearchRetriesAsync(
                        tool,
                        parsedInput,
                        result,
                        retryDetail => callbacks?.OnRunStep?.Invoke(
                            AgentRunState.GatheringContext,
                            "Evidence: search retry",
                            retryDetail),
                        ct);
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
                            callbacks?.OnRunStep?.Invoke(
                                AgentRunState.RecordingChanges,
                                "Evidence: file changed",
                                $"{change.RelativePath} ({change.Summary})");
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

                    replayEntries.Add(CreateReplayEntry(tool.Name, toolId, inputJson, result.Content, result.IsError, startedAt));
                }
                catch (Exception ex)
                {
                    var message = $"Error: {ex.Message}";
                    callbacks?.OnRunStep?.Invoke(AgentRunState.Failed, $"Tool failed: {tool.Name}", message);
                    callbacks?.OnToolError?.Invoke(tool.Name, message);
                    replayEntries.Add(CreateReplayEntry(tool.Name, toolId, inputJson, message, isError: true, startedAt));
                    results.Add(ChatContent.CreateToolResult(toolId, message, true));
                }
            }
        }

        return results;
    }

    private static IReadOnlyList<AgentVerificationPlan> ReportVerificationPlans(
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<string> executedCommands,
        ProjectMemory projectMemory,
        DesktopToolCallbacks? callbacks)
    {
        var plans = DesktopVerificationSelector.SelectPlans(fileChanges, executedCommands, projectMemory);
        foreach (var plan in plans)
        {
            callbacks?.OnVerificationPlan?.Invoke(plan);
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Verifying,
                plan.Title,
                plan.AlreadySatisfied ? plan.Reason : plan.Detail);
        }

        return plans;
    }

    private async Task SaveReplayAsync(
        string workspaceRoot,
        ProviderConfiguration config,
        string userText,
        IReadOnlyList<ToolReplayEntry> replayEntries,
        DesktopToolCallbacks? callbacks,
        CancellationToken ct)
    {
        if (replayEntries.Count == 0)
        {
            return;
        }

        var path = await _toolReplayService.SaveAsync(
            new ToolReplaySession
            {
                WorkspaceRoot = workspaceRoot,
                Provider = config.Provider,
                Model = config.Model,
                PromptPreview = TrimReplayText(userText, 800),
                Entries = replayEntries.ToList()
            },
            ct);

        if (!string.IsNullOrWhiteSpace(path))
        {
            callbacks?.OnRunStep?.Invoke(AgentRunState.Done, "Tool replay saved", path);
        }
    }

    private static ToolReplayEntry CreateReplayEntry(
        string toolName,
        string toolId,
        string inputJson,
        string result,
        bool isError,
        DateTime startedAt)
    {
        var completedAt = DateTime.UtcNow;
        return new ToolReplayEntry
        {
            StartedAt = startedAt.ToLocalTime(),
            CompletedAt = completedAt.ToLocalTime(),
            ToolName = toolName,
            ToolUseId = toolId,
            InputJson = TrimReplayText(inputJson, 8000),
            ResultPreview = TrimReplayText(result, 8000),
            IsError = isError,
            DurationMs = Math.Max(0, (int)(completedAt - startedAt).TotalMilliseconds)
        };
    }

    private static string TrimReplayText(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars] + Environment.NewLine + "[replay text truncated]";
    }

    private static void ReportConfidence(
        string responseText,
        int toolCallCount,
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<string> executedCommands,
        IReadOnlyList<AgentVerificationPlan> verificationPlans,
        int touchedMemoryCount,
        DesktopToolCallbacks? callbacks)
    {
        var confidence = DesktopConfidenceAssessor.Assess(
            responseText,
            toolCallCount,
            fileChanges,
            executedCommands,
            verificationPlans,
            touchedMemoryCount);

        callbacks?.OnRunStep?.Invoke(
            confidence.Score >= 55 ? AgentRunState.Done : AgentRunState.Failed,
            $"Confidence: {confidence.Level}",
            confidence.DisplayText);
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

        var existedBefore = File.Exists(fullPath) && !Directory.Exists(fullPath);
        var before = await ReadSnapshotTextAsync(fullPath, ct);
        return new FileSnapshot(fullPath, existedBefore, before);
    }

    private async Task<FileChangeRecord?> BuildFileChangeRecordAsync(
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

        var relativePath = Path.GetRelativePath(workspaceRoot, snapshot.FullPath).Replace('\\', '/');
        var existsAfter = File.Exists(snapshot.FullPath) && !Directory.Exists(snapshot.FullPath);
        var snapshotPath = await _fileMutationSnapshotService.SaveAsync(
            new FileMutationSnapshot
            {
                WorkspaceRoot = workspaceRoot,
                Path = snapshot.FullPath,
                RelativePath = relativePath,
                ExistedBefore = snapshot.ExistedBefore,
                ExistsAfter = existsAfter,
                Before = snapshot.Before,
                After = after
            },
            ct);

        return new FileChangeRecord
        {
            Path = snapshot.FullPath,
            RelativePath = relativePath,
            ExistedBefore = snapshot.ExistedBefore,
            Before = snapshot.Before,
            After = after,
            SnapshotPath = snapshotPath,
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

    private ToolRegistry CreateToolRegistry(ProviderConfiguration config, string workspaceRoot)
    {
        var registry = new ToolRegistry();
        registry.Register(new BashTool());
        registry.Register(new ReadFileTool());
        registry.Register(new WriteFileTool());
        registry.Register(new EditFileTool());
        registry.Register(new GrepTool());
        registry.Register(new GlobTool());
        registry.Register(new DesktopSymbolSearchTool(workspaceRoot, _symbolIndexService));
        var embeddingClient = DesktopEmbeddingClientFactory.SupportsProvider(config.EmbeddingProvider)
            ? _embeddingClientFactory.Create(config)
            : null;
        var embeddingModel = DesktopEmbeddingClientFactory.ResolveEmbeddingModel(config.EmbeddingProvider);
        registry.Register(new DesktopHybridSearchTool(
            workspaceRoot,
            _embeddingIndexStore,
            embeddingClient,
            embeddingModel,
            _symbolIndexService,
            _workspaceAnalysisService));
        if (embeddingClient != null)
        {
            registry.Register(new DesktopSemanticSearchTool(
                _embeddingIndexStore,
                embeddingClient,
                workspaceRoot,
                embeddingModel));
        }

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

internal sealed record FileSnapshot(string FullPath, bool ExistedBefore, string Before);
