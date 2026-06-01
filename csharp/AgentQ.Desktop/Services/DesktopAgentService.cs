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

public sealed class DesktopAgentService : IDesktopLlmProviderFactory
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
        For large refactors and high-risk files, use patch-sized edits instead of whole-file rewrites unless the user explicitly approves the risk.
        Treat Unity MonoBehaviour scripts, SerializeField fields, prefabs, scenes, and asset files as high risk: preserve serialized field names, prefab/Inspector assignments, and existing component relationships.
        For Unity refactors, compile after each phase and verify spawn, movement, attack, death, reward, boss, and stage progression when those systems are in scope.
        If an edit fails repeatedly, stop retrying the same strategy, reread the current file, compare the intended shape, and recover with minimal patches before suggesting manual copy-paste or destructive restore commands.
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
    private readonly ConversationCompactor _compactor = new();
    private readonly TaskDecomposer _taskDecomposer = new();
    private readonly TaskExecutor _taskExecutor;

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
        
        _taskExecutor = new TaskExecutor(
            httpClientFactory,
            linkContentFetcher,
            projectMemoryService,
            workspaceIndexer,
            embeddingIndexStore,
            embeddingClientFactory,
            fileMutationSnapshotService,
            toolReplayService,
            symbolIndexService,
            workspaceAnalysisService);
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
        CancellationToken ct = default,
        bool enableTaskDecomposition = false)
    {
        toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Planning, "Run started", "Preparing provider and workspace context.");
        var provider = CreateProvider(config);
        var effectiveWorkspaceRoot = ResolveWorkspaceRoot(workspaceRoot);
        toolCallbacks?.OnRunStep?.Invoke(AgentRunState.GatheringContext, "Gathering context", effectiveWorkspaceRoot);
        var projectMemory = await _projectMemoryService.LoadOrDiscoverAsync(effectiveWorkspaceRoot, ct);
        var projectConfig = ProjectAgentConfigService.LoadLocal(effectiveWorkspaceRoot);
        var taskProfile = DesktopPromptAssemblyService.BuildTaskProfile(userText);
        toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Planning, "Task profile", taskProfile.Label);
        var rolePlan = MultiAgentRolePlanner.Build(taskProfile);
        toolCallbacks?.OnRunStep?.Invoke(
            AgentRunState.Planning,
            "Multi-agent roles",
            string.Join(" -> ", rolePlan.Steps.Select(step => step.Role.ToString())));
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

        if (enableTaskDecomposition && 
            DesktopTaskComplexityEstimator.EstimateComplexity(userText) == TaskComplexity.Complex &&
            (taskProfile.Kind == DesktopTaskKind.Feature || taskProfile.Kind == DesktopTaskKind.Refactor || taskProfile.Kind == DesktopTaskKind.BugFix))
        {
            toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Planning, "Decomposing task", "Task classified as complex. Splitting into steps...");
            var workspaceAnalysis = await _workspaceAnalysisService.AnalyzeAsync(effectiveWorkspaceRoot, ct);
            var plan = await _taskDecomposer.DecomposeAsync(userText, workspaceAnalysis, provider, config, ct);
            
            var runResult = await _taskExecutor.ExecuteAsync(
                plan,
                config,
                effectiveWorkspaceRoot,
                permissionEnforcer ?? new DenyByDefaultPermissionEnforcer(),
                toolCallbacks,
                ct);

            return $"Task Decomposition Execution Completed. All Succeeded: {runResult.AllSucceeded}.";
        }

        _messages.Add(await CreateUserMessageAsync(userText, attachments ?? [], ct));
        var builder = new StringBuilder();
        var enforcer = permissionEnforcer ?? new DenyByDefaultPermissionEnforcer();
        var includeTransientContext = !string.IsNullOrWhiteSpace(transientContext);
        var fileChanges = new List<FileChangeRecord>();
        var executedCommands = new List<string>();
        var replayEntries = new List<ToolReplayEntry>();
        var editFailureTracker = new Dictionary<string, int>(StringComparer.Ordinal);
        var executedToolCount = 0;
        var toolRegistry = CreateToolRegistry(config, effectiveWorkspaceRoot);
        var manualFallbackRetryUsed = false;
        var genericGreetingRetryUsed = false;
        var emptyResponseRetryUsed = false;

        var maxToolSteps = ResolveMaxToolSteps(config, workMode);

        for (var step = 1; step <= maxToolSteps; step++)
        {
            if (step == maxToolSteps - 2 && toolCallbacks?.OnRequestExtendSteps != null)
            {
                var extend = toolCallbacks.OnRequestExtendSteps(maxToolSteps);
                if (extend)
                {
                    maxToolSteps += 30;
                    toolCallbacks.OnRunStep?.Invoke(AgentRunState.Planning, "Step limit extended", $"Max tool steps increased to {maxToolSteps}.");
                }
            }

            toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Generating, $"Model turn {step}", "Waiting for assistant output or tool calls.");
            
            // Compact the conversation to avoid blowing context window on long runs
            var compactedList = _compactor.Compact(_messages, maxEstimatedTokens: 80_000);
            _messages.Clear();
            _messages.AddRange(compactedList);

            var response = await GenerateAssistantTurnAsync(
                provider,
                config,
                toolRegistry,
                maxToolSteps,
                taskProfile,
                workMode,
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
                if (ShouldRetryEmptyResponse(builder.ToString(), response.ToolUses.Count))
                {
                    if (!emptyResponseRetryUsed)
                    {
                        emptyResponseRetryUsed = true;
                        _messages.Add(ChatMessage.UserText(
                            "Your previous assistant turn was empty and used no tools. Retry now. " +
                            "Use workspace tools when this is a coding task; otherwise give a concise answer. Do not return an empty response."));
                        toolCallbacks?.OnRunStep?.Invoke(
                            AgentRunState.Generating,
                            "Retrying empty response",
                            "The model returned no text and no tool calls.");
                        continue;
                    }

                    const string emptyResponseMessage = "Model response was empty. Please retry, or switch to a different model/provider if this repeats.";
                    builder.Append(emptyResponseMessage);
                    onDelta?.Invoke(emptyResponseMessage);
                    _messages.Add(ChatMessage.AssistantText(emptyResponseMessage));
                    ReportConfidence(
                        emptyResponseMessage,
                        executedToolCount,
                        fileChanges,
                        executedCommands,
                        [],
                        touchedLessons.Count,
                        replayEntries,
                        toolCallbacks);
                    toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Failed, "Empty model response", emptyResponseMessage);
                    await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
                    return emptyResponseMessage;
                }

                if (!manualFallbackRetryUsed &&
                    ShouldRetryManualFallback(builder.ToString(), executedToolCount, fileChanges, workMode))
                {
                    manualFallbackRetryUsed = true;
                    var retryInstruction =
                        "Your previous answer gave manual code or copy/paste style instructions without using available tools. " +
                        "Use the available workspace tools now: inspect the relevant files, apply the smallest safe edit, run focused verification when useful, then give a concise changed-files/root-cause/action/verification summary.";
                    _messages.Add(ChatMessage.UserText(retryInstruction));
                    toolCallbacks?.OnRunStep?.Invoke(
                        AgentRunState.Planning,
                        "Retrying with tools",
                        "Manual fallback detected before any workspace action.");
                    continue;
                }

                if (!genericGreetingRetryUsed &&
                    ShouldRetryNoToolCodingFallback(
                        userText,
                        builder.ToString(),
                        executedToolCount,
                        fileChanges,
                        workMode,
                        taskProfile.Kind))
                {
                    genericGreetingRetryUsed = true;
                    var retryInstruction =
                        "Your previous answer reset into a generic greeting or asked what to do after the user already gave a coding task. " +
                        "Do not greet or ask the same broad question. Continue the requested task now: inspect the workspace with tools, honor the latest explicit user constraints such as JavaScript over TypeScript, make the smallest useful edit, then verify.";
                    _messages.Add(ChatMessage.UserText(retryInstruction));
                    toolCallbacks?.OnRunStep?.Invoke(
                        AgentRunState.Planning,
                        "Retrying generic reset",
                        "Assistant answered with a generic greeting before using workspace tools.");
                    continue;
                }

                if (ShouldRejectNoToolCodingCompletion(
                        userText,
                        builder.ToString(),
                        executedToolCount,
                        fileChanges,
                        workMode,
                        taskProfile.Kind))
                {
                    const string noToolCompletionMessage =
                        "Coding task did not use any workspace tools, so AgentQ stopped this answer instead of treating it as complete. Please retry after ensuring workspace edit permissions are enabled.";
                    builder.AppendLine();
                    builder.Append(noToolCompletionMessage);
                    onDelta?.Invoke(Environment.NewLine + noToolCompletionMessage);
                    _messages.Add(ChatMessage.AssistantText(noToolCompletionMessage));
                    toolCallbacks?.OnRunStep?.Invoke(
                        AgentRunState.Failed,
                        "No workspace action",
                        "A coding task ended without tool use after retry.");
                    await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
                    return builder.ToString();
                }

                var verificationPlans = ReportVerificationPlans(fileChanges, executedCommands, projectMemory, toolCallbacks);
                ReportConfidence(
                    builder.ToString(),
                    executedToolCount,
                    fileChanges,
                    executedCommands,
                    verificationPlans,
                    touchedLessons.Count,
                    replayEntries,
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
                editFailureTracker,
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
            replayEntries,
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
        AgentWorkMode workMode,
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
            SystemPrompt = DesktopPromptAssemblyService.BuildSystemPrompt(
                SystemPrompt,
                taskProfile,
                DesktopToolCapabilitySnapshot.Create(toolRegistry, workMode).ToPromptBlock()),
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

    public static bool ShouldRetryManualFallback(
        string assistantText,
        int executedToolCount,
        IReadOnlyList<FileChangeRecord> fileChanges,
        AgentWorkMode workMode)
    {
        if (workMode == AgentWorkMode.Readonly ||
            executedToolCount > 0 ||
            fileChanges.Count > 0 ||
            string.IsNullOrWhiteSpace(assistantText))
        {
            return false;
        }

        var lower = assistantText.ToLowerInvariant();
        var manualInstruction =
            lower.Contains("copy and paste", StringComparison.Ordinal) ||
            lower.Contains("paste this", StringComparison.Ordinal) ||
            lower.Contains("replace with", StringComparison.Ordinal) ||
            lower.Contains("\uBCF5\uC0AC", StringComparison.Ordinal) ||
            lower.Contains("\uBD99\uC5EC\uB123", StringComparison.Ordinal) ||
            lower.Contains("\uC544\uB798\uCC98\uB7FC \uC218\uC815", StringComparison.Ordinal) ||
            lower.Contains("\uB2E4\uC74C\uCC98\uB7FC \uC218\uC815", StringComparison.Ordinal);
        var codeHeavy = lower.Contains("```", StringComparison.Ordinal) ||
            lower.Contains("```diff", StringComparison.Ordinal) ||
            lower.Contains("```csharp", StringComparison.Ordinal) ||
            lower.Contains("```cs", StringComparison.Ordinal);

        return manualInstruction && codeHeavy;
    }

    public static bool ShouldRetryGenericGreetingFallback(
        string userText,
        string assistantText,
        int executedToolCount,
        IReadOnlyList<FileChangeRecord> fileChanges,
        AgentWorkMode workMode,
        DesktopTaskKind taskKind)
    {
        if (workMode == AgentWorkMode.Readonly ||
            fileChanges.Count > 0 ||
            string.IsNullOrWhiteSpace(userText) ||
            string.IsNullOrWhiteSpace(assistantText) ||
            !IsActionableCodingTask(taskKind) ||
            !UserAskedForWorkspaceWork(userText))
        {
            return false;
        }

        var assistantLower = assistantText.ToLowerInvariant();
        return ContainsAny(
                   assistantLower,
                   "hello! what can i help",
                   "how can i help",
                   "what would you like me to",
                   "what feature would you like",
                   "which feature would you like",
                   "please tell me what feature",
                   "tell me the specific feature",
                   "no request was provided",
                   "\uC548\uB155\uD558\uC138\uC694! \uBB34\uC5C7\uC744 \uB3C4\uC640",
                   "\uBB34\uC5C7\uC744 \uB3C4\uC640\uB4DC\uB9B4\uAE4C\uC694",
                   "\uC5B4\uB5A4 \uAE30\uB2A5\uC744 \uAD6C\uD604\uD558\uACE0 \uC2F6",
                   "\uC5B4\uB5A4 \uAE30\uB2A5\uC744 \uC6D0\uD558",
                   "\uAD6C\uCCB4\uC801\uC73C\uB85C \uC5B4\uB5A4 \uAE30\uB2A5",
                   "\uC6D0\uD558\uC2DC\uB294 \uC791\uC5C5\uC744 \uC54C\uB824",
                   "\uC694\uCCAD\uD558\uC2E0 \uB0B4\uC6A9\uC774 \uC5C6\uC5B4",
                   "\uC5B4\uB5A4 \uC791\uC5C5\uC744 \uB3C4\uC640",
                   "\uC5B4\uB5A4 \uC791\uC5C5\uC744 \uC6D0\uD558") &&
            !LooksLikeWorkspaceActionSummary(assistantLower);
    }

    public static bool ShouldRetryNoToolCodingFallback(
        string userText,
        string assistantText,
        int executedToolCount,
        IReadOnlyList<FileChangeRecord> fileChanges,
        AgentWorkMode workMode,
        DesktopTaskKind taskKind)
    {
        if (workMode == AgentWorkMode.Readonly ||
            executedToolCount > 0 ||
            fileChanges.Count > 0 ||
            string.IsNullOrWhiteSpace(userText) ||
            string.IsNullOrWhiteSpace(assistantText) ||
            !IsActionableCodingTask(taskKind))
        {
            return false;
        }

        var assistantLower = assistantText.ToLowerInvariant();
        if (!UserAskedForWorkspaceWork(userText))
        {
            return false;
        }

        return !LooksLikeWorkspaceActionSummary(assistantLower);
    }

    public static bool ShouldRejectNoToolCodingCompletion(
        string userText,
        string assistantText,
        int executedToolCount,
        IReadOnlyList<FileChangeRecord> fileChanges,
        AgentWorkMode workMode,
        DesktopTaskKind taskKind) =>
        workMode != AgentWorkMode.Readonly &&
        fileChanges.Count == 0 &&
        !string.IsNullOrWhiteSpace(userText) &&
        !string.IsNullOrWhiteSpace(assistantText) &&
        IsActionableCodingTask(taskKind) &&
        UserAskedForWorkspaceWork(userText) &&
        !LooksLikeWorkspaceActionSummary(assistantText.ToLowerInvariant());

    private static bool IsActionableCodingTask(DesktopTaskKind taskKind) =>
        taskKind is DesktopTaskKind.Feature or DesktopTaskKind.BugFix or DesktopTaskKind.Refactor or DesktopTaskKind.VerificationFailure;

    private static bool UserAskedForWorkspaceWork(string userText)
    {
        var userLower = userText.ToLowerInvariant();
        return ContainsAny(
            userLower,
            "make",
            "build",
            "create",
            "implement",
            "fix",
            "python",
            "data analysis",
            "data tool",
            "portfolio",
            "homepage",
            "website",
            "\uB9CC\uB4E4",
            "\uC0DD\uC131",
            "\uAD6C\uD604",
            "\uACE0\uCCD0",
            "\uC218\uC815",
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624",
            "\uD648\uD398\uC774\uC9C0",
            "\uC6F9\uC0AC\uC774\uD2B8",
            "\uC790\uBC14\uC2A4\uD06C\uB9BD\uD2B8",
            "\uD30C\uC774\uC36C",
            "\uB370\uC774\uD130 \uBD84\uC11D",
            "\uBD84\uC11D \uB3C4\uAD6C");
    }

    private static bool LooksLikeWorkspaceActionSummary(string assistantLower)
    {
        return ContainsAny(
            assistantLower,
            "changed",
            "created",
            "updated",
            "modified",
            "wrote",
            "edited",
            "ran ",
            "test passed",
            "build passed",
            "\uBCC0\uACBD",
            "\uC0DD\uC131",
            "\uC218\uC815\uD588",
            "\uAD6C\uD604\uD588",
            "\uD14C\uC2A4\uD2B8 \uD1B5\uACFC",
            "\uBE4C\uB4DC \uD1B5\uACFC");
    }

    public static bool ShouldRetryEmptyResponse(string assistantText, int toolUseCount) =>
        toolUseCount == 0 && string.IsNullOrWhiteSpace(assistantText);

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
            ? await _workspaceIndexer.BuildContextAsync(workspaceRoot, userText, ct)
            : string.Empty;
        var linkedContext = config.DesktopAutoFetchLinks
            ? await _linkContentFetcher.BuildContextAsync(userText, ct)
            : string.Empty;
        var memoryContext = _projectMemoryService.BuildContext(projectMemory, userText);
        var mcpContext = McpServerRegistry.BuildContext(projectConfig);
        var hasLinkIntent = HasLinkIntent(userText);
        var linkStatusContext = BuildLinkStatusContext(config, userText, linkedContext, hasLinkIntent);
        var explicitStackContext = BuildExplicitStackPreferenceContext(userText);
        var scaffoldDecisionContext = await BuildScaffoldDecisionContextAsync(workspaceRoot, taskProfile, ct);

        if (string.IsNullOrWhiteSpace(workspaceContext) &&
            string.IsNullOrWhiteSpace(linkedContext) &&
            string.IsNullOrWhiteSpace(memoryContext) &&
            string.IsNullOrWhiteSpace(mcpContext) &&
            string.IsNullOrWhiteSpace(linkStatusContext) &&
            string.IsNullOrWhiteSpace(explicitStackContext) &&
            string.IsNullOrWhiteSpace(scaffoldDecisionContext))
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
        builder.AppendLine(DesktopExecutionStrategyCatalog.ForProfile(taskProfile).FormatForPrompt());
        builder.AppendLine("Codebase discovery hint: use hybrid_search first when you need ranked candidate files with reasons.");
        builder.AppendLine("Code navigation hint: use symbol_search for known or likely identifiers before broad grep; then read_file the best candidate.");
        builder.AppendLine("Search fallback order: symbol_search for definitions, semantic_search for meaning-based context when enabled, grep_search/glob_search for broad fallback.");
        builder.AppendLine("Evidence-backed analysis rule: when answering project analysis or documentation questions, cite the inspected files or commands in a short Evidence section and put unsupported inferences under Needs verification.");
        builder.AppendLine("Link capability rule: AgentQ Desktop can attempt to fetch HTTP/HTTPS URLs when link auto-read is enabled. Never say AgentQ cannot access URLs categorically.");

        if (!string.IsNullOrWhiteSpace(explicitStackContext))
        {
            builder.AppendLine(explicitStackContext);
        }

        if (!string.IsNullOrWhiteSpace(linkStatusContext))
        {
            builder.AppendLine(linkStatusContext);
        }

        if (!string.IsNullOrWhiteSpace(scaffoldDecisionContext))
        {
            builder.AppendLine();
            builder.AppendLine(scaffoldDecisionContext);
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

    private async Task<string> BuildScaffoldDecisionContextAsync(
        string workspaceRoot,
        DesktopTaskProfile taskProfile,
        CancellationToken ct)
    {
        if (taskProfile.Kind != DesktopTaskKind.Feature)
        {
            return string.Empty;
        }

        try
        {
            var analysis = await _workspaceAnalysisService.AnalyzeAsync(workspaceRoot, ct);
            var builder = new StringBuilder();
            builder.AppendLine("Scaffold decision context:");
            builder.AppendLine("Available scaffold candidates are optional references. The assistant must decide whether to ask a focused question, implement manually with file tools, or mirror a candidate structure.");

            if (analysis.ScaffoldRecommendations.Count == 0)
            {
                builder.AppendLine("No exact worker scaffold candidate was found for this workspace. If the task is still clear, implement the requested files manually with workspace tools.");
                return builder.ToString().TrimEnd();
            }

            foreach (var recommendation in analysis.ScaffoldRecommendations.Take(4))
            {
                var files = recommendation.Files.Count == 0
                    ? "no file list"
                    : string.Join(", ", recommendation.Files.Take(8));
                var commands = recommendation.VerificationCommands.Count == 0
                    ? "no verification command"
                    : string.Join(", ", recommendation.VerificationCommands.Take(3));
                builder.AppendLine($"- {recommendation.Name}: {recommendation.Description}; files: {files}; verify: {commands}");
            }

            return builder.ToString().TrimEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string BuildExplicitStackPreferenceContext(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return string.Empty;
        }

        var text = userText.ToLowerInvariant();
        if (ContainsAny(text, "javascript", "java script", "\uC790\uBC14\uC2A4\uD06C\uB9BD\uD2B8", "\uC790\uBC14 \uC2A4\uD06C\uB9BD\uD2B8", "\uC790\uBC14\uC2A4\uD06C\uB9BD\uD2B8\uB85C", "\uC790\uBC14 \uC2A4\uD06C\uB9BD\uD2B8\uB85C", " js", ".js", ".jsx"))
        {
            return "Current user stack override: JavaScript was explicitly requested in this turn. Treat this as a hard constraint for implementation and final wording. Create or modify .js/.jsx files and do not choose TypeScript because the workspace, dashboard, memory, or earlier assistant recommendation mentioned TypeScript.";
        }

        if (ContainsAny(text, "typescript", "type script", "\uD0C0\uC785\uC2A4\uD06C\uB9BD\uD2B8", "\uD0C0\uC785 \uC2A4\uD06C\uB9BD\uD2B8", " ts", ".ts", ".tsx"))
        {
            return "Current user stack override: TypeScript was explicitly requested in this turn. TypeScript files are acceptable for this task.";
        }

        return string.Empty;
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

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static async Task<ChatMessage> CreateUserMessageAsync(
        string userText,
        IReadOnlyList<DesktopAttachment> attachments,
        CancellationToken ct)
    {
        var content = new List<ChatContent> { ChatContent.CreateText(userText) };
        var visualNotes = VisualEvidenceService.BuildPromptNotes(attachments);
        if (visualNotes.Count > 0)
        {
            content.Add(ChatContent.CreateText(string.Join(Environment.NewLine, visualNotes)));
        }

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

    public ILlmProvider CreateProvider(ProviderConfiguration config)
    {
        ILlmProvider provider = config.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), ResolveModel(config, "gpt-4o")),
            "opencode-go" => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), ResolveModel(config, "gpt-4o"), name: "opencode-go"),
            "anthropic" => new AnthropicProvider(CreateAnthropicClient(config.BaseUrl), config.ApiKey),
            _ => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), ResolveModel(config, "gpt-4o"), name: config.Provider)
        };

        return new ResilientLlmProvider(provider);
    }

    private static string ResolveModel(ProviderConfiguration config, string fallback)
    {
        return string.IsNullOrWhiteSpace(config.Model) ? fallback : config.Model;
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
        Dictionary<string, int> editFailureTracker,
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
                if (ShouldStopRepeatedEditStrategy(tool.Name, parsedInput, editFailureTracker, out var recoveryMessage))
                {
                    callbacks?.OnRunStep?.Invoke(AgentRunState.Failed, "Edit recovery guard", recoveryMessage);
                    callbacks?.OnToolError?.Invoke(tool.Name, recoveryMessage);
                    replayEntries.Add(CreateReplayEntry(tool.Name, toolId, JsonSerializer.Serialize(parsedInput), recoveryMessage, isError: true, DateTime.UtcNow));
                    results.Add(ChatContent.CreateToolResult(toolId, recoveryMessage, true));
                    continue;
                }

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
                        RecordEditFailure(tool.Name, parsedInput, editFailureTracker, callbacks);
                    }
                    else
                    {
                        callbacks?.OnToolOutput?.Invoke(tool.Name, result.Content);
                        if (ShellVerificationResultDetector.TryCreate(tool.Name, parsedInput, result.Content, out var verificationResult))
                        {
                            callbacks?.OnVerificationResult?.Invoke(verificationResult);
                            callbacks?.OnRunStep?.Invoke(
                                AgentRunState.Verifying,
                                $"Verification passed: {verificationResult.Title}",
                                verificationResult.Summary);
                        }
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

    private static bool ShouldStopRepeatedEditStrategy(
        string toolName,
        Dictionary<string, object?> input,
        IReadOnlyDictionary<string, int> editFailureTracker,
        out string recoveryMessage)
    {
        recoveryMessage = string.Empty;
        if (!IsFileMutationTool(toolName))
        {
            return false;
        }

        var key = BuildEditFailureKey(toolName, input);
        if (key == null || !editFailureTracker.TryGetValue(key, out var failures) || failures < 2)
        {
            return false;
        }

        recoveryMessage = "Repeated edit failure detected for the same file and strategy. " +
                          "Stop retrying this exact edit, reread the current file, compare the intended shape, " +
                          "then recover with a smaller patch. Before suggesting git restore or checkout, inspect git diff for the file, " +
                          "consider a backup copy, and warn that local changes would be discarded.";
        return true;
    }

    private static void RecordEditFailure(
        string toolName,
        Dictionary<string, object?> input,
        Dictionary<string, int> editFailureTracker,
        DesktopToolCallbacks? callbacks)
    {
        if (!IsFileMutationTool(toolName))
        {
            return;
        }

        var key = BuildEditFailureKey(toolName, input);
        if (key == null)
        {
            return;
        }

        editFailureTracker[key] = editFailureTracker.TryGetValue(key, out var count) ? count + 1 : 1;
        if (editFailureTracker[key] == 2)
        {
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Failed,
                "Edit recovery needed",
                "The same edit strategy failed twice. Reread the file and continue with a smaller patch or recovery plan.");
        }
    }

    private static string? BuildEditFailureKey(string toolName, Dictionary<string, object?> input)
    {
        if (!TryGetString(input, "path", out var path))
        {
            return null;
        }

        if (string.Equals(toolName, "edit_file", StringComparison.OrdinalIgnoreCase))
        {
            var oldString = TryGetString(input, "old_string", out var oldValue) ? oldValue : string.Empty;
            var replaceAll = input.TryGetValue("replace_all", out var rawReplaceAll) && rawReplaceAll is true;
            return $"{toolName}|{path}|{replaceAll}|{oldString}";
        }

        return $"{toolName}|{path}|whole-file";
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object?> input, string key, out string value)
    {
        value = string.Empty;
        if (!input.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is string stringValue)
        {
            value = stringValue;
            return true;
        }

        if (rawValue is JsonElement json && json.ValueKind == JsonValueKind.String)
        {
            value = json.GetString() ?? string.Empty;
            return true;
        }

        return false;
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
        IReadOnlyList<ToolReplayEntry> replayEntries,
        DesktopToolCallbacks? callbacks)
    {
        var confidence = DesktopConfidenceAssessor.Assess(
            responseText,
            toolCallCount,
            fileChanges,
            executedCommands,
            verificationPlans,
            touchedMemoryCount,
            replayEntries);

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

        RegisterMcpTools(registry, workspaceRoot);
        registry.Register(new PluginEchoTool());
        return registry;
    }

    private static void RegisterMcpTools(ToolRegistry registry, string workspaceRoot)
    {
        var projectConfig = ProjectAgentConfigService.LoadLocal(workspaceRoot);
        var servers = McpServerRegistry.EnabledServers(projectConfig, workspaceRoot);
        if (servers.Count == 0)
        {
            return;
        }

        var client = new StdioMcpClient();
        foreach (var server in servers.Take(4))
        {
            IReadOnlyList<McpToolInfo> tools;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                tools = client.ListToolsAsync(server, cts.Token).GetAwaiter().GetResult();
            }
            catch
            {
                continue;
            }

            foreach (var tool in tools.Take(16))
            {
                registry.Register(new McpBridgeTool(
                    McpToolName.Build(server.Name, tool.Name),
                    server,
                    tool,
                    client));
            }
        }
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
