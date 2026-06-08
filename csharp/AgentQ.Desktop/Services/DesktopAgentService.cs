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
        Answer the user's direct request first; do not introduce AgentQ identity, model-provider details, tool inventories, or capability explanations unless the user explicitly asks about them.
        Do not begin by saying you cannot remember previous conversations, that this is a new session, or that you need to recover context; answer the current request from the visible message and available workspace evidence.
        Do not output hidden reasoning, thinking blocks, prompt fragments, or planning metadata as user-visible text.
        For feasibility questions, answer the feasibility briefly (one sentence), then immediately inspect the workspace with list_directory or read_file before suggesting next steps. Do not end the response after stating feasibility.
        When you find yourself saying "I need to check X first" or "\uD655\uC778\uD574\uC57C", call the appropriate inspection tool (list_directory, read_file) instead of writing another sentence. Text is not a check; tool output is.
        If explicitly asked who developed AgentQ or who made you, answer that AgentQ was developed by robot0971-art.
        If explicitly asked whether you are Kimi, Moonshot AI, OpenAI, Anthropic, DeepSeek, or another model provider, explain that model providers are only the underlying inference engines used by AgentQ.
        If explicitly asked about the underlying model, mention the selected provider or model separately.
        Answer in Korean by default unless the user asks for another language.
        Assume the user is working on Windows. Prefer safe, concise guidance.
        You can use tools to read files, search the workspace, edit files, write files, and run shell commands.
        Prefer inspecting files before editing. After making code changes, run focused build or test commands when useful.
        Prefer hybrid_search for codebase discovery because it combines symbol, semantic, keyword, and project-map evidence.
        For code navigation, prefer symbol_search first when the user mentions a function, class, component, method, or likely identifier.
        Use list_directory for folder structure and empty-folder checks, read_file for known files, semantic_search when embeddings are available and the request is meaning-based, and grep_search/glob_search for broad text or file pattern fallback.
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
        For URL questions only: AgentQ Desktop can attempt to read HTTP/HTTPS links when link auto-read is enabled. Do not claim categorical inability to access external URLs; describe the current link auto-read setting, fetch result, or fallback instead.
        """;

    private const int DefaultMaxToolSteps = 50;
    private const int ReadonlyMaxToolSteps = 20;
    private const int MaxConfiguredToolSteps = 100;
    private const int MaxToolResultChars = 24000;
    private const int MaxChangeSnapshotChars = 160000;
    private const int MaxTextAttachmentChars = 60000;
    private const int RepeatedReadOnlyToolLimit = 3;
    private const string ToolOutputDirectoryName = "tool-output";

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
    private readonly SystemSkillService _systemSkillService;
    private readonly List<ChatMessage> _messages = [];
    private readonly ConversationCompactor _compactor = new();
    private readonly TaskDecomposer _taskDecomposer = new();
    private readonly ProjectScaffoldPlanner _projectScaffoldPlanner = new();
    private readonly ProjectScaffoldPlanRegistry _projectScaffoldPlanRegistry = new();
    private readonly ExecutionLessonMemoryService _executionLessonMemoryService = new();
    private readonly DesktopLocalServerService _localServerService;
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
        WorkspaceAnalysisService workspaceAnalysisService,
        DesktopLocalServerService? localServerService = null,
        SystemSkillService? systemSkillService = null)
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
        _systemSkillService = systemSkillService ?? new SystemSkillService();
        _localServerService = localServerService ?? new DesktopLocalServerService(httpClientFactory);
        
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
        var effectiveWorkspaceRoot = ResolveWorkspaceRoot(workspaceRoot);
        toolCallbacks?.OnRunStep?.Invoke(AgentRunState.GatheringContext, "Gathering context", effectiveWorkspaceRoot);
        var projectMemory = await _projectMemoryService.LoadOrDiscoverAsync(effectiveWorkspaceRoot, ct);
        var projectConfig = ProjectAgentConfigService.LoadLocal(effectiveWorkspaceRoot);
        var taskProfile = DesktopPromptAssemblyService.BuildTaskProfile(userText);
        toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Planning, "Task profile", taskProfile.Label);
        var ruleTurnIntent = TurnIntentClassifier.Classify(userText);
        var turnIntent = await ClassifyTurnIntentWithModelAsync(config, userText, ruleTurnIntent, toolCallbacks, ct);
        toolCallbacks?.OnRunStep?.Invoke(
            AgentRunState.Planning,
            $"Turn intent: {turnIntent.Type}",
            $"{turnIntent.Rationale} action={turnIntent.ActionKind}; confidence={turnIntent.Confidence:0.00}; concrete={turnIntent.IsConcreteEnough}");
        var taskContract = UserIntentTranslator.Translate(userText);
        if (turnIntent.Type != TurnIntentType.Conversation && taskContract.IsActionable)
        {
            toolCallbacks?.OnRunStep?.Invoke(
                AgentRunState.Planning,
                $"Task contract: {taskContract.Intent}",
                taskContract.Goal);
        }

        var projectScaffoldPlan = turnIntent.Type is TurnIntentType.Action or TurnIntentType.Hybrid
            ? _projectScaffoldPlanRegistry.Register(_projectScaffoldPlanner.Plan(userText, effectiveWorkspaceRoot), effectiveWorkspaceRoot)
            : new ProjectScaffoldPlanningResult
            {
                IsGreenfieldRequest = false,
                CanProceed = false,
                Reasons = [$"Turn intent is {turnIntent.Type}, so scaffold planning was skipped before execution."]
            };
        var selectedSystemSkills = _systemSkillService.SelectRelevantSkills(userText, effectiveWorkspaceRoot, taskProfile, projectConfig);
        var skillToolUseRequired =
            turnIntent.Type is TurnIntentType.Action or TurnIntentType.Hybrid &&
            SystemSkillService.RequiresToolUseForFileProducingTask(selectedSystemSkills, userText, taskProfile);
        if (turnIntent.Type == TurnIntentType.Ambiguous)
        {
            var clarification = string.IsNullOrWhiteSpace(turnIntent.ClarifyingQuestion)
                ? "Please clarify the target and desired result before AgentQ executes anything."
                : turnIntent.ClarifyingQuestion;
            _messages.Add(await CreateUserMessageAsync(userText, attachments ?? [], ct));
            _messages.Add(ChatMessage.AssistantText(clarification));
            onDelta?.Invoke(clarification);
            toolCallbacks?.OnRunStep?.Invoke(
                AgentRunState.Clarifying,
                "Waiting for user answer",
                turnIntent.Rationale);
            return clarification;
        }

        if (workMode != AgentWorkMode.Readonly &&
            taskProfile.Kind == DesktopTaskKind.Feature &&
            projectScaffoldPlan.IsGreenfieldRequest &&
            !projectScaffoldPlan.CanProceed)
        {
            var clarification = string.IsNullOrWhiteSpace(projectScaffoldPlan.ClarifyingQuestion)
                ? "What kind of project would you like to create? (어떤 종류의 프로젝트를 원하시나요?) Examples: portfolio website, Python data analysis tool, game, API server, wordbook web app."
                : projectScaffoldPlan.ClarifyingQuestion;
            _messages.Add(await CreateUserMessageAsync(userText, attachments ?? [], ct));
            _messages.Add(ChatMessage.AssistantText(clarification));
            onDelta?.Invoke(clarification);
            toolCallbacks?.OnRunStep?.Invoke(
                AgentRunState.Clarifying,
                "Waiting for user answer",
                string.Join(" ", projectScaffoldPlan.Reasons.DefaultIfEmpty("The project request is underspecified, so AgentQ asked a focused project-type question before calling a provider.")));
            return clarification;
        }

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
        var transientContext = await BuildContextOnlyAsync(config, userText, effectiveWorkspaceRoot, projectMemory, projectConfig, taskProfile, projectScaffoldPlan, selectedSystemSkills, ct);
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
            turnIntent.AllowsDeterministicExecution &&
            !ShouldExecuteSafeScaffoldDirectly(projectScaffoldPlan, workMode) &&
            DesktopTaskComplexityEstimator.EstimateComplexity(userText) == TaskComplexity.Complex &&
            (taskProfile.Kind == DesktopTaskKind.Feature || taskProfile.Kind == DesktopTaskKind.Refactor || taskProfile.Kind == DesktopTaskKind.BugFix))
        {
            var decompositionProvider = CreateProvider(config, toolCallbacks);
            toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Planning, "Decomposing task", "Task classified as complex. Splitting into steps...");
            var workspaceAnalysis = await _workspaceAnalysisService.AnalyzeAsync(effectiveWorkspaceRoot, ct);
            var plan = await _taskDecomposer.DecomposeAsync(userText, workspaceAnalysis, decompositionProvider, config, ct);
            
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

        if (turnIntent.AllowsDeterministicExecution &&
            ShouldExecuteLocalServerDirectly(taskContract, workMode))
        {
            toolCallbacks?.OnRunStep?.Invoke(
                AgentRunState.Planning,
                "Local server mode",
                "AgentQ Desktop will manage the local development server directly from the task contract.");
            var localServerSummary = string.Empty;
            var localServerSucceeded = false;
            if (taskContract.Intent == TaskContractIntent.StopLocalServer)
            {
                var stopResult = await _localServerService.StopAsync(
                    effectiveWorkspaceRoot,
                    enforcer,
                    toolCallbacks,
                    ct);
                localServerSucceeded = stopResult.Succeeded;
                localServerSummary = BuildLocalServerStopSummary(stopResult);
                toolCallbacks?.OnLocalServerChanged?.Invoke(new DesktopLocalServerState(
                    IsRunning: false,
                    Url: stopResult.Url,
                    Command: string.Empty,
                    ProcessId: stopResult.ProcessId,
                    ReusedExisting: false,
                    Message: stopResult.Message));
            }
            else
            {
                var localServerResult = await _localServerService.StartAsync(
                    effectiveWorkspaceRoot,
                    enforcer,
                    toolCallbacks,
                    ct);
                if (!string.IsNullOrWhiteSpace(localServerResult.Command))
                {
                    executedCommands.Add(localServerResult.Command);
                }

                localServerSucceeded = localServerResult.Succeeded;
                localServerSummary = BuildLocalServerSummary(localServerResult);
                toolCallbacks?.OnLocalServerChanged?.Invoke(new DesktopLocalServerState(
                    IsRunning: localServerResult.Succeeded,
                    Url: localServerResult.Url,
                    Command: localServerResult.Command,
                    ProcessId: localServerResult.ProcessId,
                    ReusedExisting: localServerResult.ReusedExisting,
                    Message: localServerResult.Message));
            }

            builder.Clear();
            builder.Append(localServerSummary);
            onDelta?.Invoke(localServerSummary);
            _messages.Add(ChatMessage.AssistantText(localServerSummary));
            if (localServerSucceeded)
            {
                await _executionLessonMemoryService.RecordContractSuccessAsync(effectiveWorkspaceRoot, taskContract, ct);
            }
            else
            {
                await _executionLessonMemoryService.RecordContractFailureAsync(effectiveWorkspaceRoot, taskContract, userText, localServerSummary, ct);
            }

            ReportConfidence(
                builder.ToString(),
                executedToolCount,
                fileChanges,
                executedCommands,
                [],
                touchedLessons.Count,
                replayEntries,
                toolCallbacks);
            await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
            return builder.ToString();
        }

        if (turnIntent.AllowsDeterministicExecution &&
            ShouldExecuteSafeScaffoldDirectly(projectScaffoldPlan, workMode))
        {
            toolCallbacks?.OnRunStep?.Invoke(
                AgentRunState.Planning,
                "Safe scaffold mode",
                "Approved scaffold creation will be executed by AgentQ Desktop instead of relying on the model to call scaffold tools.");
            var scaffoldSummary = await ExecutePreparedProjectScaffoldPrimaryAsync(
                projectScaffoldPlan,
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
            builder.Clear();
            builder.Append(scaffoldSummary);
            onDelta?.Invoke(scaffoldSummary);
            _messages.Add(ChatMessage.AssistantText(scaffoldSummary));
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
            toolCallbacks?.OnRunStep?.Invoke(
                AgentRunState.Done,
                "Run complete",
                "Safe scaffold mode finished after deterministic project creation.");
            await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
            return builder.ToString();
        }

        var provider = CreateProvider(config, toolCallbacks);
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

            var bufferTextUntilValidated =
                workMode != AgentWorkMode.Readonly &&
                turnIntent.Type != TurnIntentType.Conversation &&
                IsActionableCodingTask(taskProfile.Kind) &&
                executedToolCount == 0 &&
                fileChanges.Count == 0;
            var response = await GenerateAssistantTurnAsync(
                provider,
                config,
                toolRegistry,
                maxToolSteps,
                taskProfile,
                workMode,
                includeTransientContext ? transientContext : null,
                streamTextDeltas: !bufferTextUntilValidated,
                onDelta,
                toolCallbacks?.OnUsage,
                ct);
            toolCallbacks?.OnRunStep?.Invoke(
                AgentRunState.Generating,
                "Model turn output",
                BuildAssistantTurnDiagnostic(
                    step,
                    response,
                    bufferTextUntilValidated,
                    executedToolCount,
                    fileChanges,
                    builder.Length));
            includeTransientContext = false;
            if (response.ToolUses.Count == 0 &&
                !bufferTextUntilValidated &&
                !string.IsNullOrEmpty(response.AssistantText))
            {
                builder.Append(response.AssistantText);
            }

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
                var candidateText = bufferTextUntilValidated
                    ? response.AssistantText
                    : builder.ToString();
                if (ShouldRetryEmptyResponse(candidateText, response.ToolUses.Count))
                {
                    if (!emptyResponseRetryUsed)
                    {
                        emptyResponseRetryUsed = true;
                        builder.Clear();
                        _messages.Add(ChatMessage.UserText(skillToolUseRequired
                            ? "Your previous assistant turn was empty and used no tools. Retry now. An active AgentQ system skill requires tool use for this file-producing task; call the appropriate workspace/scaffold tools instead of answering in prose."
                            : "Your previous assistant turn was empty and used no tools. Retry now. Use workspace tools when this is a coding task; otherwise give a concise answer. Do not return an empty response."));
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
                    ShouldRetryManualFallback(candidateText, executedToolCount, fileChanges, workMode))
                {
                    manualFallbackRetryUsed = true;
                    builder.Clear();
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

                var shouldRetryNoToolCoding =
                    turnIntent.Type != TurnIntentType.Conversation &&
                    ShouldRetryNoToolCodingFallback(
                        userText,
                        candidateText,
                        executedToolCount,
                        fileChanges,
                        workMode,
                        taskProfile.Kind,
                        skillToolUseRequired);
                var shouldRetryGenericGreeting =
                    turnIntent.Type != TurnIntentType.Conversation &&
                    ShouldRetryGenericGreetingFallback(
                        userText,
                        candidateText,
                        executedToolCount,
                        fileChanges,
                        workMode,
                        taskProfile.Kind);

                if (!genericGreetingRetryUsed &&
                    turnIntent.Type != TurnIntentType.Conversation &&
                    TaskContractCompletionChecker.ShouldRetry(taskContract, candidateText, executedCommands, workMode))
                {
                    genericGreetingRetryUsed = true;
                    builder.Clear();
                    var retryInstruction = TaskContractCompletionChecker.BuildRetryInstruction(taskContract);
                    await _executionLessonMemoryService.RecordContractFailureAsync(effectiveWorkspaceRoot, taskContract, userText, candidateText, ct);
                    _messages.Add(ChatMessage.UserText(retryInstruction));
                    toolCallbacks?.OnRunStep?.Invoke(
                        AgentRunState.Planning,
                        "Task contract: retry",
                        BuildNoToolGuardDetail(
                            "retry",
                            $"Assistant answer did not satisfy task contract {taskContract.Intent}.",
                            userText,
                            candidateText,
                            executedToolCount,
                            fileChanges,
                            workMode,
                            taskProfile.Kind,
                            skillToolUseRequired));
                    continue;
                }

                if (turnIntent.Type != TurnIntentType.Conversation &&
                    TaskContractCompletionChecker.ShouldReject(taskContract, candidateText, executedCommands, workMode))
                {
                    var message = $"The answer did not satisfy the current task contract ({taskContract.Intent}). Please retry; AgentQ should {taskContract.Goal}";
                    await _executionLessonMemoryService.RecordContractFailureAsync(effectiveWorkspaceRoot, taskContract, userText, candidateText, ct);
                    builder.Clear();
                    builder.Append(message);
                    onDelta?.Invoke(message);
                    _messages.Add(ChatMessage.AssistantText(message));
                    toolCallbacks?.OnRunStep?.Invoke(
                        AgentRunState.Failed,
                        "Task contract: rejected",
                        BuildNoToolGuardDetail(
                            "rejected",
                            $"Assistant answer did not satisfy task contract {taskContract.Intent}.",
                            userText,
                            candidateText,
                            executedToolCount,
                            fileChanges,
                            workMode,
                            taskProfile.Kind,
                            skillToolUseRequired));
                    await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
                    return builder.ToString();
                }

                if (!genericGreetingRetryUsed &&
                    (shouldRetryNoToolCoding || shouldRetryGenericGreeting))
                {
                    genericGreetingRetryUsed = true;
                    builder.Clear();
                    var retryInstruction = BuildNoToolRetryInstruction(projectScaffoldPlan, skillToolUseRequired);
                    _messages.Add(ChatMessage.UserText(retryInstruction));
                    var retryReason = HasProceedableProjectScaffoldPlan(projectScaffoldPlan)
                        ? "Assistant answered without calling create_project_scaffold for a prepared scaffold plan."
                        : skillToolUseRequired
                            ? "An active system skill requires workspace/scaffold tool use for this file-producing task."
                            : "Assistant answered with a generic greeting before using workspace tools.";
                    toolCallbacks?.OnRunStep?.Invoke(
                        AgentRunState.Planning,
                        shouldRetryGenericGreeting ? "Greeting guard: retry" : "No-tool guard: retry",
                        BuildNoToolGuardDetail(
                            "retry",
                            $"{retryReason} triggerNoTool={shouldRetryNoToolCoding}; triggerGenericGreeting={shouldRetryGenericGreeting}; retryInstruction=\"{DesktopPromptBuilder.Truncate(retryInstruction.ReplaceLineEndings(" "), 500)}\"",
                            userText,
                            candidateText,
                            executedToolCount,
                            fileChanges,
                            workMode,
                            taskProfile.Kind,
                            skillToolUseRequired));
                    continue;
                }

                if (turnIntent.Type != TurnIntentType.Conversation &&
                    ShouldRejectNoToolCodingCompletion(
                        userText,
                        candidateText,
                        executedToolCount,
                        fileChanges,
                        workMode,
                        taskProfile.Kind,
                        skillToolUseRequired))
                {
                    var noToolCompletionMessage = BuildNoToolCompletionMessage(projectScaffoldPlan, skillToolUseRequired);
                    builder.Clear();
                    builder.Append(noToolCompletionMessage);
                    onDelta?.Invoke(noToolCompletionMessage);
                    _messages.Add(ChatMessage.AssistantText(noToolCompletionMessage));
                    toolCallbacks?.OnRunStep?.Invoke(
                        AgentRunState.Failed,
                        "No-tool guard: rejected",
                        BuildNoToolGuardDetail(
                            "rejected",
                            "A coding task ended without tool use after retry.",
                            userText,
                            candidateText,
                            executedToolCount,
                            fileChanges,
                            workMode,
                            taskProfile.Kind,
                            skillToolUseRequired));
                    await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
                    return builder.ToString();
                }

                if (IsAllowedClarification(userText, candidateText.ToLowerInvariant()))
                {
                    if (bufferTextUntilValidated)
                    {
                        builder.Append(candidateText);
                        onDelta?.Invoke(candidateText);
                    }

                    toolCallbacks?.OnRunStep?.Invoke(
                        AgentRunState.Clarifying,
                        "Waiting for user answer",
                        "The project request is underspecified, so AgentQ asked a focused project-type question.");
                    await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
                    return builder.ToString();
                }

                if (HasProceedableProjectScaffoldPlan(projectScaffoldPlan) &&
                    workMode != AgentWorkMode.Readonly &&
                    fileChanges.Count == 0)
                {
                    var scaffoldText = await ExecutePreparedProjectScaffoldPrimaryAsync(
                        projectScaffoldPlan,
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
                    if (!string.IsNullOrWhiteSpace(scaffoldText))
                    {
                        builder.Clear();
                        builder.Append(scaffoldText);
                        onDelta?.Invoke(scaffoldText);
                        _messages.Add(ChatMessage.AssistantText(scaffoldText));
                        await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
                        return builder.ToString();
                    }
                }

                if (bufferTextUntilValidated)
                {
                    toolCallbacks?.OnRunStep?.Invoke(
                        AgentRunState.Done,
                        "No-tool guard: allowed",
                        BuildNoToolGuardDetail(
                            "allowed",
                            "Completion was shown even though no workspace tools or file changes were recorded.",
                            userText,
                            candidateText,
                            executedToolCount,
                            fileChanges,
                            workMode,
                            taskProfile.Kind,
                            skillToolUseRequired));
                }

                if (bufferTextUntilValidated)
                {
                    builder.Append(candidateText);
                    onDelta?.Invoke(candidateText);
                }

                if (ShouldRunProjectScaffoldVerificationFallback(
                        projectScaffoldPlan,
                        fileChanges,
                        replayEntries,
                        workMode))
                {
                    await ExecutePreparedProjectScaffoldVerificationAsync(
                        projectScaffoldPlan,
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
                }

                var verificationPlans = ReportVerificationPlans(fileChanges, executedCommands, projectMemory, toolCallbacks);
                if (ShouldReplaceIrrelevantFinalAfterChanges(builder.ToString(), fileChanges, workMode, taskProfile.Kind))
                {
                    var replacementText = BuildFileChangeCompletionSummary(fileChanges, executedCommands, verificationPlans);
                    builder.Clear();
                    builder.Append(replacementText);
                    onDelta?.Invoke(replacementText);
                    _messages.Add(ChatMessage.AssistantText(replacementText));
                    toolCallbacks?.OnRunStep?.Invoke(
                        AgentRunState.Done,
                        "Final answer guard: replaced",
                        "The model's final answer did not match the recorded file changes, so AgentQ replaced it with a deterministic change summary.");
                }

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
                ct,
                turnIntent);
            if (toolResults.Count > 0)
            {
                _messages.Add(new ChatMessage
                {
                    Role = ChatRole.User,
                    Content = toolResults
                });
            }

            if (TryBuildProjectScaffoldCollisionSummary(toolResults, out var scaffoldCollisionSummary))
            {
                builder.Clear();
                builder.Append(scaffoldCollisionSummary);
                onDelta?.Invoke(scaffoldCollisionSummary);
                _messages.Add(ChatMessage.AssistantText(scaffoldCollisionSummary));
                toolCallbacks?.OnRunStep?.Invoke(
                    AgentRunState.Done,
                    "Project scaffold collision: stopped",
                    "create_project_scaffold reported existing target files, so AgentQ stopped the model loop instead of continuing into unrelated work.");
                ReportConfidence(
                    builder.ToString(),
                    executedToolCount,
                    fileChanges,
                    executedCommands,
                    [],
                    touchedLessons.Count,
                    replayEntries,
                    toolCallbacks);
                toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Done, "Run complete", "Assistant stopped after project scaffold file collision.");
                await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
                return builder.ToString();
            }

            if (ShouldRunProjectScaffoldFallbackAfterPermissionDenied(
                    toolResults,
                    replayEntries,
                    fileChanges,
                    projectScaffoldPlan))
            {
                var scaffoldSummary = await ExecutePreparedProjectScaffoldPrimaryAsync(
                    projectScaffoldPlan,
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
                var verificationPlans = ReportVerificationPlans(fileChanges, executedCommands, projectMemory, toolCallbacks);
                builder.Clear();
                builder.Append(scaffoldSummary);
                onDelta?.Invoke(scaffoldSummary);
                _messages.Add(ChatMessage.AssistantText(scaffoldSummary));
                toolCallbacks?.OnRunStep?.Invoke(
                    AgentRunState.Done,
                    "Permission fallback: scaffold executed",
                    "A non-scaffold tool was blocked, so AgentQ used the prepared scaffold plan instead of continuing unrelated model turns.");
                ReportConfidence(
                    builder.ToString(),
                    executedToolCount,
                    fileChanges,
                    executedCommands,
                    verificationPlans,
                    touchedLessons.Count,
                    replayEntries,
                    toolCallbacks);
                toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Done, "Run complete", "Assistant finished after permission fallback.");
                await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
                return builder.ToString();
            }

            if (ShouldStopAfterReadOnlyLoopGuard(toolResults, fileChanges, HasProceedableProjectScaffoldPlan(projectScaffoldPlan)))
            {
                if (ShouldRunProjectScaffoldVerificationFallback(
                        projectScaffoldPlan,
                        fileChanges,
                        replayEntries,
                        workMode))
                {
                    await ExecutePreparedProjectScaffoldVerificationAsync(
                        projectScaffoldPlan,
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
                }

                var verificationPlans = ReportVerificationPlans(fileChanges, executedCommands, projectMemory, toolCallbacks);
                var loopSummary = string.Empty;
                if (HasProceedableProjectScaffoldPlan(projectScaffoldPlan) && fileChanges.Count == 0)
                {
                    loopSummary = await ExecutePreparedProjectScaffoldPrimaryAsync(
                        projectScaffoldPlan,
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
                }

                if (string.IsNullOrWhiteSpace(loopSummary))
                {
                    loopSummary = BuildFileChangeCompletionSummary(fileChanges, executedCommands, verificationPlans);
                }

                builder.Clear();
                builder.Append(loopSummary);
                onDelta?.Invoke(loopSummary);
                _messages.Add(ChatMessage.AssistantText(loopSummary));
                toolCallbacks?.OnRunStep?.Invoke(
                    AgentRunState.Done,
                    "Read-only loop guard: stopped",
                    "A repeated read-only tool loop was detected after file changes, so AgentQ stopped the model loop and summarized recorded changes.");
                ReportConfidence(
                    builder.ToString(),
                    executedToolCount,
                    fileChanges,
                    executedCommands,
                    verificationPlans,
                    touchedLessons.Count,
                    replayEntries,
                    toolCallbacks);
                toolCallbacks?.OnRunStep?.Invoke(AgentRunState.Done, "Run complete", "Assistant finished after repeated read-only tool loop guard.");
                await SaveReplayAsync(effectiveWorkspaceRoot, config, userText, replayEntries, toolCallbacks, ct);
                return builder.ToString();
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
        bool streamTextDeltas,
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
                if (streamTextDeltas)
                {
                    onDelta?.Invoke(chunk.TextDelta);
                }
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
        return new DesktopAssistantTurn(assistantText.ToString(), assistantContent, toolUses);
    }

    private async Task<TurnIntentClassification> ClassifyTurnIntentWithModelAsync(
        ProviderConfiguration config,
        string userText,
        TurnIntentClassification ruleClassification,
        DesktopToolCallbacks? callbacks,
        CancellationToken ct)
    {
        if (!TurnIntentClassifier.ShouldAskModel(ruleClassification) ||
            !HasConfiguredProviderEndpoint(config))
        {
            return ruleClassification;
        }

        try
        {
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Planning,
                "LLM intent classifier",
                $"Rule classification was {ruleClassification.Type} with confidence {ruleClassification.Confidence:0.00}; asking the model for a structured second opinion.");

            var provider = CreateProvider(config, callbacks);
            var context = new ChatContext
            {
                Model = ResolveModel(config, provider.DefaultModel),
                SystemPrompt = BuildTurnIntentClassifierPrompt(),
                Messages =
                [
                    ChatMessage.UserText(BuildTurnIntentClassifierInput(userText, ruleClassification))
                ],
                MaxTokens = 512,
                Stream = false,
                MaxSteps = 1
            };

            var response = await provider.GenerateResponseAsync(context, [], ct);
            if (response.Usage != null)
            {
                callbacks?.OnUsage?.Invoke(response.Usage);
            }

            var responseText = string.Join(
                Environment.NewLine,
                response.Content
                    .Where(content => content.Type == ContentType.Text)
                    .Select(content => content.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));

            if (!TurnIntentClassifier.TryParseModelResponse(responseText, ruleClassification, out var modelClassification))
            {
                callbacks?.OnRunStep?.Invoke(
                    AgentRunState.Planning,
                    "LLM intent classifier fallback",
                    "The model did not return valid intent JSON, so AgentQ kept the rule-based classification.");
                return ruleClassification;
            }

            var merged = TurnIntentClassifier.MergeModelClassification(ruleClassification, modelClassification);
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Planning,
                $"LLM intent result: {merged.Type}",
                $"{merged.Rationale} action={merged.ActionKind}; confidence={merged.Confidence:0.00}; concrete={merged.IsConcreteEnough}");
            return merged;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Planning,
                "LLM intent classifier fallback",
                $"Intent classification model call failed, so AgentQ kept the rule-based classification. {ex.Message}");
            return ruleClassification;
        }
    }

    private static bool HasConfiguredProviderEndpoint(ProviderConfiguration config)
    {
        return !string.IsNullOrWhiteSpace(config.BaseUrl);
    }

    private static string BuildTurnIntentClassifierPrompt()
    {
        return
            """
            You classify one AgentQ user turn before any local execution.
            Return exactly one JSON object and no markdown.
            Valid type values: Conversation, Action, Hybrid, Ambiguous.

            Definitions:
            - Conversation: explanation, advice, comparison, review, learning, feasibility, opinion, design discussion, or meta feedback about AgentQ.
            - Action: concrete request to create, edit, delete, run, build, test, install, commit, scaffold, or mutate local state.
            - Hybrid: action first, then explanation, summary, or report.
            - Ambiguous: action-like wording, but the target, stack, workspace, approval, or desired output is not concrete enough.

            Safety:
            - Prefer Conversation or Ambiguous when uncertain.
            - "how to", "방법 알려줘", "어떻게 하면", "어떻게 좋을까", "괜찮을까", "가능할까" are usually Conversation unless the user clearly asks AgentQ to execute.
            - Do not classify meta feedback such as permission dialog complaints as Action.

            JSON shape:
            {
              "type": "Conversation|Action|Hybrid|Ambiguous",
              "confidence": 0.0,
              "rationale": "short reason",
              "actionKind": "create|edit|delete|shell|git|search|file|",
              "requiresWrite": false,
              "requiresShell": false,
              "requiresNetwork": false,
              "isConcreteEnough": false,
              "clarifyingQuestion": ""
            }
            """;
    }

    private static string BuildTurnIntentClassifierInput(
        string userText,
        TurnIntentClassification ruleClassification)
    {
        return
            $"""
            User turn:
            {userText}

            Rule-based first pass:
            type={ruleClassification.Type}
            confidence={ruleClassification.Confidence:0.00}
            actionKind={ruleClassification.ActionKind}
            requiresWrite={ruleClassification.RequiresWrite}
            requiresShell={ruleClassification.RequiresShell}
            requiresNetwork={ruleClassification.RequiresNetwork}
            isConcreteEnough={ruleClassification.IsConcreteEnough}
            rationale={ruleClassification.Rationale}

            Classify the user turn for AgentQ routing.
            """;
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

    public static bool ShouldReplaceIrrelevantFinalAfterChanges(
        string assistantText,
        IReadOnlyList<FileChangeRecord> fileChanges,
        AgentWorkMode workMode,
        DesktopTaskKind taskKind)
    {
        if (workMode == AgentWorkMode.Readonly ||
            fileChanges.Count == 0 ||
            string.IsNullOrWhiteSpace(assistantText) ||
            !IsActionableCodingTask(taskKind))
        {
            return false;
        }

        var lower = assistantText.ToLowerInvariant();
        if (LooksLikeWorkspaceActionSummary(lower))
        {
            return false;
        }

        return ContainsAny(
            lower,
            "\uBB38\uC11C \uB0B4\uC6A9\uC774 \uBE44\uC5B4",
            "\uBB38\uC11C\uC758 \uC804\uCCB4 \uB0B4\uC6A9",
            "# [ ]",
            "document content is empty",
            "document is empty",
            "send the full document",
            "\uB9CC\uB4E4\uC5B4\uB4DC\uB9AC\uACA0\uC2B5\uB2C8\uB2E4",
            "\uBA3C\uC800 \uC6CC\uD06C\uC2A4\uD398\uC774\uC2A4",
            "\uBA3C\uC800 \uD604\uC7AC \uC6CC\uD06C\uC2A4\uD398\uC774\uC2A4",
            "\uBA87 \uAC00\uC9C0 \uC9C8\uBB38",
            "\uD604\uC7AC \uC6CC\uD06C\uC2A4\uD398\uC774\uC2A4\uB97C \uD655\uC778",
            "\uB2E4\uC774\uC5B4\uD2B8",
            "\uCE7C\uB85C\uB9AC",
            "\uCCB4\uC911",
            "diet",
            "calorie",
            "weight tracking",
            "i will build",
            "i will create",
            "first, i will check",
            "hello! what can i help",
            "what can i help",
            "how can i help",
            "\uC548\uB155\uD558\uC138\uC694! \uBB34\uC5C7\uC744",
            "\uBB34\uC5C7\uC744 \uB3C4\uC640\uB4DC\uB9B4\uAE4C\uC694",
            "cors(cross-origin",
            "same-origin policy");
    }

    public static string BuildFileChangeCompletionSummary(
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<string> executedCommands,
        IReadOnlyList<AgentVerificationPlan> verificationPlans)
    {
        var summary = new StringBuilder();
        summary.AppendLine("작업은 완료됐지만 최종 모델 응답이 기록된 파일 변경과 맞지 않아 AgentQ가 변경 내역을 대신 요약했습니다.");
        summary.AppendLine();
        summary.AppendLine("변경된 파일:");
        foreach (var change in fileChanges.Take(12))
        {
            summary.AppendLine($"- {change.RelativePath} ({change.Summary})");
        }

        if (fileChanges.Count > 12)
        {
            summary.AppendLine($"- ...외 {fileChanges.Count - 12}개");
        }

        summary.AppendLine();
        if (executedCommands.Count > 0)
        {
            summary.AppendLine("실행된 명령:");
            foreach (var command in executedCommands.Take(6))
            {
                summary.AppendLine($"- {command}");
            }
        }
        else
        {
            summary.AppendLine("검증 명령은 기록되지 않았습니다.");
            if (verificationPlans.Count > 0)
            {
                summary.AppendLine("제안된 검증:");
                foreach (var plan in verificationPlans.Take(6))
                {
                    summary.AppendLine(string.IsNullOrWhiteSpace(plan.Command)
                        ? $"- {plan.Title}"
                        : $"- {plan.Command}");
                }
            }
        }

        return summary.ToString().TrimEnd();
    }

    public static bool ShouldStopAfterReadOnlyLoopGuard(
        IReadOnlyList<ChatContent> toolResults,
        IReadOnlyList<FileChangeRecord> fileChanges,
        bool hasProceedableProjectScaffoldPlan = false)
    {
        return (fileChanges.Count > 0 || hasProceedableProjectScaffoldPlan) &&
               toolResults.Any(result =>
                   result.IsToolError == true &&
                   !string.IsNullOrWhiteSpace(result.ToolResult) &&
                   result.ToolResult.Contains("Repeated read-only tool call detected", StringComparison.OrdinalIgnoreCase));
    }

    public static bool ShouldRunProjectScaffoldFallbackAfterPermissionDenied(
        IReadOnlyList<ChatContent> toolResults,
        IReadOnlyList<ToolReplayEntry> replayEntries,
        IReadOnlyList<FileChangeRecord> fileChanges,
        ProjectScaffoldPlanningResult projectScaffoldPlan)
    {
        if (!HasProceedableProjectScaffoldPlan(projectScaffoldPlan) || fileChanges.Count > 0)
        {
            return false;
        }

        if (!toolResults.Any(result =>
                result.IsToolError == true &&
                !string.IsNullOrWhiteSpace(result.ToolResult) &&
                result.ToolResult.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var deniedTool = replayEntries.LastOrDefault(entry =>
            entry.IsError &&
            entry.ResultPreview.Contains("Permission denied", StringComparison.OrdinalIgnoreCase));
        return deniedTool != null &&
               !string.Equals(deniedTool.ToolName, "create_project_scaffold", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(deniedTool.ToolName, "verify_project_scaffold", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryBuildProjectScaffoldCollisionSummary(
        IReadOnlyList<ChatContent> toolResults,
        out string summary)
    {
        summary = string.Empty;
        foreach (var result in toolResults)
        {
            if (string.IsNullOrWhiteSpace(result.ToolResult))
            {
                continue;
            }

            var scaffold = ProjectScaffoldToolSummary.Parse(result.ToolResult);
            if (scaffold.Succeeded ||
                scaffold.SkippedFiles.Count == 0 ||
                !scaffold.Issues.Any(issue =>
                    issue.Contains("already exist", StringComparison.OrdinalIgnoreCase) ||
                    issue.Contains("target files", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            summary = BuildProjectScaffoldCollisionSummary(scaffold);
            return true;
        }

        return false;
    }

    private static string BuildProjectScaffoldCollisionSummary(ProjectScaffoldToolSummary scaffold)
    {
        var builder = new StringBuilder();
        builder.AppendLine("프로젝트 생성은 진행하지 않았습니다.");
        builder.AppendLine();
        builder.AppendLine("대상 파일이 이미 있어서 덮어쓰지 않았습니다:");
        foreach (var file in scaffold.SkippedFiles.Take(12))
        {
            builder.AppendLine($"- {file}");
        }

        if (scaffold.SkippedFiles.Count > 12)
        {
            builder.AppendLine($"- ...외 {scaffold.SkippedFiles.Count - 12}개");
        }

        builder.AppendLine();
        builder.AppendLine("기존 파일을 보존하려면 빈 폴더를 선택하세요. 같은 폴더에서 다시 만들려면 덮어쓰기 승인이 필요합니다.");
        return builder.ToString().TrimEnd();
    }

    public static bool TryBuildPreflightClarification(
        string userText,
        AgentWorkMode workMode,
        DesktopTaskKind taskKind,
        out string message)
    {
        message = string.Empty;
        if (workMode == AgentWorkMode.Readonly ||
            taskKind != DesktopTaskKind.Feature ||
            !IsBareNewProjectRequest(userText))
        {
            return false;
        }

        message =
            "새 프로젝트를 만들 수 있습니다. 다만 아직 어떤 프로젝트인지 정해지지 않았기 때문에 바로 스택이나 파일을 고르지는 않겠습니다.\n\n" +
            "어떤 종류의 프로젝트를 원하시나요? 예: 포트폴리오 홈페이지, Python 데이터 분석 도구, 게임, API 서버, 단어장 웹앱.";
        return true;
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
            !IsActionableCodingTask(taskKind))
        {
            return false;
        }

        var assistantLower = assistantText.ToLowerInvariant();
        if (executedToolCount == 0 && EndsWithGenericGreeting(assistantLower))
        {
            return true;
        }

        if (!UserAskedForWorkspaceWork(userText))
        {
            return false;
        }

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
                   "\uC5B4\uB5A4 \uC791\uC5C5\uC744 \uC6D0\uD558",
                   "\uC800\uB294 kimi",
                   "\uC800\uB294 moonshot",
                   "\uC800\uB294 openai",
                   "\uC81C \uC2DC\uC2A4\uD15C \uD504\uB86C\uD504\uD2B8",
                   "\uC2DC\uC2A4\uD15C \uD504\uB86C\uD504\uD2B8\uB294",
                   "\uD2B9\uC815 \uBAA8\uB378 \uC81C\uACF5\uC790\uAC00 \uC544\uB2C8\uB77C",
                   "\uC81C\uAC00 \uAC00\uC9C4 \uD234 \uBAA9\uB85D",
                   "\uD234 \uBAA9\uB85D",
                   "\uD30C\uC77C \uC77D\uAE30",
                   "\uD30C\uC77C \uC4F0\uAE30",
                   "\uD30C\uC77C \uD3B8\uC9D1",
                   "i am not kimi",
                   "i am not openai",
                   "my system prompt",
                   "system prompt is",
                   "tool list",
                   "available tools",
                   "read_file",
                   "write_file",
                   "grep_search") &&
            !LooksLikeWorkspaceActionSummary(assistantLower);
    }

    private static bool EndsWithGenericGreeting(string assistantLower)
    {
        var normalized = assistantLower.Trim();
        normalized = normalized.TrimEnd('.', '!', '?', '\u3002', '\uFF01', '\uFF1F');
        return normalized.EndsWith("what can i help", StringComparison.Ordinal) ||
               normalized.EndsWith("how can i help", StringComparison.Ordinal) ||
               normalized.EndsWith("what would you like me to do", StringComparison.Ordinal) ||
               normalized.EndsWith("\uBB34\uC5C7\uC744 \uB3C4\uC640\uB4DC\uB9B4\uAE4C\uC694", StringComparison.Ordinal) ||
               normalized.EndsWith("\uC5B4\uB5A4 \uC791\uC5C5\uC744 \uB3C4\uC640\uB4DC\uB9B4\uAE4C\uC694", StringComparison.Ordinal) ||
               normalized.EndsWith("\uC5B4\uB5A4 \uC791\uC5C5\uC744 \uC6D0\uD558\uC2DC\uB098\uC694", StringComparison.Ordinal);
    }

    private static bool HasProceedableProjectScaffoldPlan(ProjectScaffoldPlanningResult projectScaffoldPlan) =>
        projectScaffoldPlan.IsGreenfieldRequest &&
        projectScaffoldPlan.CanProceed &&
        projectScaffoldPlan.Intent != null &&
        projectScaffoldPlan.Plan != null;

    public static bool ShouldExecuteSafeScaffoldDirectly(
        ProjectScaffoldPlanningResult projectScaffoldPlan,
        AgentWorkMode workMode)
    {
        return workMode != AgentWorkMode.Readonly &&
               HasProceedableProjectScaffoldPlan(projectScaffoldPlan) &&
               !string.IsNullOrWhiteSpace(projectScaffoldPlan.PlanId) &&
               !string.IsNullOrWhiteSpace(projectScaffoldPlan.PlanHash);
    }

    public static bool ShouldExecuteLocalServerDirectly(TaskContract taskContract, AgentWorkMode workMode)
    {
        return workMode != AgentWorkMode.Readonly &&
               taskContract.IsActionable &&
               taskContract.Intent is TaskContractIntent.RunLocalServer or TaskContractIntent.StopLocalServer;
    }

    private static string BuildLocalServerSummary(LocalServerStartResult result)
    {
        if (result.Succeeded)
        {
            var builder = new StringBuilder();
            builder.AppendLine("로컬 개발 서버를 띄웠습니다.");
            builder.AppendLine();
            builder.AppendLine($"URL: {result.Url}");
            if (!string.IsNullOrWhiteSpace(result.Command))
            {
                builder.AppendLine($"Command: {result.Command}");
            }

            if (result.ProcessId > 0)
            {
                builder.AppendLine($"Process ID: {result.ProcessId}");
            }

            return builder.ToString().TrimEnd();
        }

        return string.IsNullOrWhiteSpace(result.Message)
            ? "로컬 개발 서버를 띄우지 못했습니다."
            : "로컬 개발 서버를 띄우지 못했습니다. " + result.Message;
    }

    private static string BuildLocalServerStopSummary(LocalServerStopResult result)
    {
        if (result.Succeeded)
        {
            return string.IsNullOrWhiteSpace(result.Url)
                ? result.Message
                : $"로컬 개발 서버를 종료했습니다.{Environment.NewLine}{Environment.NewLine}URL: {result.Url}";
        }

        return string.IsNullOrWhiteSpace(result.Message)
            ? "로컬 개발 서버를 종료하지 못했습니다."
            : "로컬 개발 서버를 종료하지 못했습니다. " + result.Message;
    }

    public static string BuildNoToolRetryInstruction(
        ProjectScaffoldPlanningResult projectScaffoldPlan,
        bool skillToolUseRequired = false)
    {
        if (HasProceedableProjectScaffoldPlan(projectScaffoldPlan))
        {
            return
                "The desktop preflight already produced a project scaffold plan. Do not answer in prose. " +
                "Call create_project_scaffold now with the attached planId and planHash. " +
                "After it succeeds, call verify_project_scaffold with the same planId and planHash. " +
                "If create_project_scaffold reports existing file collisions, report the collision and ask before overwrite.";
        }

        if (skillToolUseRequired)
        {
            return
                "An active AgentQ system skill requires tool use for this file-producing task. " +
                "Do not answer in prose and do not emit raw file contents in code blocks. " +
                "Use the available workspace/scaffold tools now: inspect the workspace if needed, create or edit the requested files with tools, then run the relevant verification command.";
        }

        return
            "Your previous answer reset into a generic greeting or asked what to do after the user already gave a coding task. " +
            "Do not describe your system prompt, identity, or tool inventory. Continue the requested task now: call list_directory first if you need folder structure or empty-workspace evidence, then inspect relevant files with read_file/search tools, honor the latest explicit user constraints such as JavaScript over TypeScript, make the smallest useful edit, then verify.";
    }

    public static string BuildNoToolCompletionMessage(
        ProjectScaffoldPlanningResult projectScaffoldPlan,
        bool skillToolUseRequired = false)
    {
        if (HasProceedableProjectScaffoldPlan(projectScaffoldPlan))
        {
            return "Project scaffold plan was prepared, but the model did not call create_project_scaffold. Please retry; AgentQ should call create_project_scaffold with the approved planId and planHash, then verify_project_scaffold.";
        }

        return skillToolUseRequired
            ? "An active AgentQ system skill required workspace/scaffold tool use for this file-producing task, but the model did not call tools after retry. Please retry; AgentQ should use the requested skill flow with workspace/scaffold tools instead of answering in prose."
            : "Coding task did not use workspace tools after retry, so AgentQ stopped this answer instead of showing an unsupported completion. Please retry; AgentQ should use list_directory/read_file/search tools before answering workspace tasks.";
    }

    private static string BuildNoToolGuardDetail(
        string outcome,
        string reason,
        string userText,
        string assistantText,
        int executedToolCount,
        IReadOnlyList<FileChangeRecord> fileChanges,
        AgentWorkMode workMode,
        DesktopTaskKind taskKind,
        bool skillToolUseRequired)
    {
        return
            $"outcome={outcome}; reason={reason}; workMode={workMode}; taskKind={taskKind}; " +
            $"tools={executedToolCount}; changes={fileChanges.Count}; skillToolUseRequired={skillToolUseRequired}; " +
            $"user=\"{DesktopPromptBuilder.Truncate(userText.ReplaceLineEndings(" "), 240)}\"; " +
            $"assistant=\"{DesktopPromptBuilder.Truncate(assistantText.ReplaceLineEndings(" "), 360)}\"";
    }

    private static string BuildAssistantTurnDiagnostic(
        int step,
        DesktopAssistantTurn response,
        bool bufferTextUntilValidated,
        int executedToolCount,
        IReadOnlyList<FileChangeRecord> fileChanges,
        int accumulatedTextLength)
    {
        var assistantText = response.AssistantText ?? string.Empty;
        var assistantLower = assistantText.ToLowerInvariant();
        var toolNames = response.ToolUses
            .Select(tool => tool.ToolName ?? string.Empty)
            .Where(tool => !string.IsNullOrWhiteSpace(tool))
            .ToList();

        return
            $"step={step}; assistantChars={assistantText.Length}; toolUses={response.ToolUses.Count}; " +
            $"toolNames={string.Join(",", toolNames.DefaultIfEmpty("none"))}; assistantContent={response.AssistantContent.Count}; " +
            $"bufferTextUntilValidated={bufferTextUntilValidated}; executedToolsBeforeTurn={executedToolCount}; " +
            $"fileChanges={fileChanges.Count}; accumulatedTextBeforeTurn={accumulatedTextLength}; " +
            $"endsWithGenericGreeting={EndsWithGenericGreeting(assistantLower)}; " +
            $"looksLikeHalfValidFeasibilityGreeting={LooksLikeHalfValidFeasibilityGreeting(assistantLower)}; " +
            $"looksLikeTextOnlyInspectionClaim={LooksLikeTextOnlyInspectionClaim(assistantLower)}; " +
            $"assistantPreview=\"{DesktopPromptBuilder.Truncate(assistantText.ReplaceLineEndings(" "), 700)}\"";
    }

    public static bool ShouldRetryNoToolCodingFallback(
        string userText,
        string assistantText,
        int executedToolCount,
        IReadOnlyList<FileChangeRecord> fileChanges,
        AgentWorkMode workMode,
        DesktopTaskKind taskKind,
        bool skillToolUseRequired = false)
    {
        if (workMode == AgentWorkMode.Readonly ||
            fileChanges.Count > 0 ||
            string.IsNullOrWhiteSpace(userText) ||
            string.IsNullOrWhiteSpace(assistantText) ||
            !IsActionableCodingTask(taskKind))
        {
            return false;
        }

        var assistantLower = assistantText.ToLowerInvariant();
        if (LooksLikeHalfValidFeasibilityGreeting(assistantLower) ||
            LooksLikeTextOnlyInspectionClaim(assistantLower))
        {
            return true;
        }

        if (!skillToolUseRequired &&
            LooksLikeConsultativeCodingQuestion(userText.ToLowerInvariant()) &&
            UserAskedForWorkspaceWork(userText))
        {
            return !LooksLikeWorkspaceActionSummary(assistantLower);
        }

        if (!skillToolUseRequired &&
            !UserAskedForMutationWork(userText))
        {
            return false;
        }

        if (IsAllowedClarification(userText, assistantLower))
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
        DesktopTaskKind taskKind,
        bool skillToolUseRequired = false)
    {
        if (workMode == AgentWorkMode.Readonly ||
            fileChanges.Count > 0 ||
            string.IsNullOrWhiteSpace(userText) ||
            string.IsNullOrWhiteSpace(assistantText) ||
            !IsActionableCodingTask(taskKind))
        {
            return false;
        }

        var userLower = userText.ToLowerInvariant();
        var assistantLower = assistantText.ToLowerInvariant();
        if (!skillToolUseRequired &&
            LooksLikeConsultativeCodingQuestion(userLower) &&
            !UserAskedForWorkspaceWork(userText))
        {
            return false;
        }

        if (!skillToolUseRequired &&
            !UserAskedForMutationWork(userText) &&
            !UserAskedForWorkspaceWork(userText))
        {
            return false;
        }

        if (IsAllowedClarification(userText, assistantLower))
        {
            return false;
        }

        return !LooksLikeWorkspaceActionSummary(assistantLower);
    }

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
            "write",
            "unreal",
            "playercontroller",
            "player controller",
            "c++",
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
            "\uB2E8\uC5B4\uC7A5",
            "\uC790\uBC14\uC2A4\uD06C\uB9BD\uD2B8",
            "\uD30C\uC774\uC36C",
            "\uB370\uC774\uD130 \uBD84\uC11D",
            "\uBD84\uC11D \uB3C4\uAD6C");
    }

    private static bool LooksLikeHalfValidFeasibilityGreeting(string assistantLower) =>
        ContainsAny(
            assistantLower,
            "\uAC00\uB2A5\uD569\uB2C8\uB2E4",
            "\uB124, \uAC00\uB2A5",
            "\uB124 \uAC00\uB2A5",
            "yes, it is possible",
            "yes, possible",
            "it is possible") &&
        ContainsAny(
            assistantLower,
            "\uBB34\uC5C7\uC744 \uB3C4\uC640\uB4DC\uB9B4\uAE4C\uC694",
            "\uC5B4\uB5A4 \uC791\uC5C5\uC744 \uB3C4\uC640",
            "\uC5B4\uB5A4 \uC791\uC5C5\uC744 \uC6D0\uD558",
            "what can i help",
            "how can i help",
            "what would you like");

    private static bool LooksLikeTextOnlyInspectionClaim(string assistantLower) =>
        ContainsAny(
            assistantLower,
            "\uBA3C\uC800 \uD655\uC778\uD574\uC57C",
            "\uD655\uC778\uD574\uC57C",
            "\uBA3C\uC800 \uC0B4\uD3B4",
            "\uC0B4\uD3B4\uBD10\uC57C",
            "need to check",
            "i need to check",
            "must check",
            "need to inspect",
            "i need to inspect",
            "must inspect");

    private static bool UserAskedForMutationWork(string userText)
    {
        var userLower = userText.ToLowerInvariant();
        if (LooksLikeConsultativeCodingQuestion(userLower))
        {
            return false;
        }

        if (ContainsAny(
                userLower,
                "why",
                "why is",
                "what is",
                "what do you think",
                "how does",
                "analyze this",
                "review this",
                "is it done",
                "is this done",
                "done?",
                "\uC65C",
                "\uBB50\uAC00 \uBB38\uC81C",
                "\uC5B4\uB5BB\uAC8C \uC0DD\uAC01",
                "\uC774 \uBD84\uC11D",
                "\uC5B4\uB5BB\uB098",
                "\uB2E4 \uB41C",
                "\uB05D\uB09C",
                "\uBB50\uD558\uBA74",
                "\uC774\uC81C"))
        {
            return false;
        }

        return ContainsAny(
            userLower,
            "make",
            "build",
            "create",
            "implement",
            "fix",
            "modify",
            "update",
            "add ",
            "scaffold",
            "generate",
            "proceed",
            "continue",
            "next task",
            "\uB9CC\uB4E4",
            "\uC0DD\uC131",
            "\uAD6C\uD604",
            "\uC791\uC131",
            "\uACE0\uCCD0",
            "\uC218\uC815",
            "\uCD94\uAC00",
            "\uC9C4\uD589",
            "\uB2E4\uC74C \uC791\uC5C5",
            "\uD574\uBCF4\uC790",
            "\uD558\uC790") ||
            UserAskedForWorkspaceWork(userText);
    }

    private static bool LooksLikeConsultativeCodingQuestion(string userLower)
    {
        if (ContainsAny(
                userLower,
                "make it now",
                "build it now",
                "create it now",
                "implement it now",
                "go ahead",
                "please create",
                "please implement",
                "\uBC14\uB85C \uB9CC\uB4E4",
                "\uBC14\uB85C \uC0DD\uC131",
                "\uBC14\uB85C \uAD6C\uD604",
                "\uC774\uB300\uB85C \uB9CC\uB4E4",
                "\uC774\uB300\uB85C \uC0DD\uC131",
                "\uC774\uB300\uB85C \uAD6C\uD604",
                "\uB9CC\uB4E4\uC5B4\uC918",
                "\uC0DD\uC131\uD574\uC918",
                "\uAD6C\uD604\uD574\uC918",
                "\uC9C4\uD589\uD574"))
        {
            return false;
        }

        return ContainsAny(
            userLower,
            "is it possible",
            "would it be possible",
            "can i",
            "can we",
            "can you",
            "can this",
            "can it",
            "could i",
            "could we",
            "could you",
            "possible?",
            "\uAC00\uB2A5\uD55C\uAC00",
            "\uAC00\uB2A5\uD560\uAE4C",
            "\uAC00\uB2A5\uD574",
            "\uAC00\uB2A5\uD558\uB0D0",
            "\uAC00\uB2A5",
            "\uD560 \uC218 \uC788",
            "\uD574\uC904\uC218 \uC788",
            "\uD574\uC904 \uC218 \uC788",
            "\uC904\uC218 \uC788",
            "\uC904 \uC218 \uC788",
            "\uD574\uBCFC \uC218 \uC788",
            "\uC5B4\uB5A8\uAE4C",
            "\uAD1C\uCC2E\uC744\uAE4C");
    }

    private static bool IsAllowedClarification(string userText, string assistantLower)
    {
        return IsBareNewProjectRequest(userText) && LooksLikeFocusedProjectClarification(assistantLower);
    }

    private static bool IsBareNewProjectRequest(string userText)
    {
        var userLower = userText.ToLowerInvariant();
        var asksForProject = ContainsAny(
            userLower,
            "new project",
            "project",
            "\uC0C8\uB85C\uC6B4 \uD504\uB85C\uC81D\uD2B8",
            "\uC0C8 \uD504\uB85C\uC81D\uD2B8",
            "\uD504\uB85C\uC81D\uD2B8");
        if (!asksForProject)
        {
            return false;
        }

        return !ContainsAny(
            userLower,
            "portfolio",
            "website",
            "homepage",
            "python",
            "fastapi",
            "api",
            "game",
            "cli",
            "data",
            "\uD3EC\uD2B8\uD3F4\uB9AC\uC624",
            "\uC6F9\uC0AC\uC774\uD2B8",
            "\uD648\uD398\uC774\uC9C0",
            "\uD30C\uC774\uC36C",
            "\uAC8C\uC784",
            "\uB370\uC774\uD130",
            "\uB2E8\uC5B4\uC7A5");
    }

    private static bool LooksLikeFocusedProjectClarification(string assistantLower)
    {
        return ContainsAny(
            assistantLower,
            "what kind of project",
            "what type of project",
            "which kind of project",
            "which type of project",
            "\uC5B4\uB5A4 \uC885\uB958\uC758 \uD504\uB85C\uC81D\uD2B8",
            "\uC5B4\uB5A4 \uD504\uB85C\uC81D\uD2B8",
            "\uC6D0\uD558\uC2DC\uB294 \uD504\uB85C\uC81D\uD2B8 \uC885\uB958",
            "\uD504\uB85C\uC81D\uD2B8 \uC885\uB958");
    }

    private static bool LooksLikeWorkspaceActionSummary(string assistantLower)
    {
        if (LooksLikeFutureWorkPromise(assistantLower))
        {
            return false;
        }

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
            "\uBCC0\uACBD\uD588",
            "\uBCC0\uACBD\uB428",
            "\uBCC0\uACBD \uC644\uB8CC",
            "\uC0DD\uC131\uD588",
            "\uC0DD\uC131\uB428",
            "\uC0DD\uC131 \uC644\uB8CC",
            "\uC0DD\uC131\uB41C \uD30C\uC77C",
            "\uC218\uC815\uD588",
            "\uAD6C\uD604\uD588",
            "\uD14C\uC2A4\uD2B8 \uD1B5\uACFC",
            "\uBE4C\uB4DC \uD1B5\uACFC");
    }

    private static bool LooksLikeFutureWorkPromise(string assistantLower)
    {
        return ContainsAny(
            assistantLower,
            "i will",
            "i can create",
            "i can build",
            "i'll",
            "will create",
            "will build",
            "\uC0DD\uC131\uD558\uACA0\uC2B5\uB2C8\uB2E4",
            "\uB9CC\uB4E4\uACA0\uC2B5\uB2C8\uB2E4",
            "\uB9CC\uB4E4\uC5B4 \uB4DC\uB9AC\uACA0\uC2B5\uB2C8\uB2E4",
            "\uAD6C\uD604\uD558\uACA0\uC2B5\uB2C8\uB2E4",
            "\uC9C4\uD589\uD558\uACA0\uC2B5\uB2C8\uB2E4");
    }

    public static bool ShouldRetryEmptyResponse(string assistantText, int toolUseCount) =>
        toolUseCount == 0 && string.IsNullOrWhiteSpace(assistantText);

    private async Task<string> ExecutePreparedProjectScaffoldPrimaryAsync(
        ProjectScaffoldPlanningResult projectScaffoldPlan,
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
        if (!HasProceedableProjectScaffoldPlan(projectScaffoldPlan))
        {
            return string.Empty;
        }

        callbacks?.OnRunStep?.Invoke(
            AgentRunState.Planning,
            "Safe scaffold execution",
            "AgentQ is executing the registered scaffold plan as the primary new-project creation path after permission approval.");

        var createResults = await ExecuteToolsAsync(
            [
                ChatContent.CreateToolUse(
                    "auto_create_project_scaffold_" + Guid.NewGuid().ToString("N"),
                    "create_project_scaffold",
                    BuildProjectScaffoldCreateInputJson(projectScaffoldPlan))
            ],
            toolRegistry,
            enforcer,
            callbacks,
            workspaceRoot,
            workMode,
            fileChanges,
            executedCommands,
            replayEntries,
            editFailureTracker,
            ct);

        var createSummary = ProjectScaffoldToolSummary.Parse(createResults.FirstOrDefault()?.ToolResult);
        if (createResults.FirstOrDefault()?.IsToolError == true)
        {
            return "Project scaffold creation failed: " + (createResults.FirstOrDefault()?.ToolResult ?? "unknown error");
        }

        if (!createSummary.Succeeded &&
            createSummary.CreatedFiles.Count == 0 &&
            createSummary.SkippedFiles.Count > 0)
        {
            return BuildProjectScaffoldCollisionSummary(createSummary);
        }

        var verifySummaries = new List<ProjectScaffoldToolSummary>();
        if (createSummary.Succeeded && createSummary.VerificationCommands.Count > 0)
        {
            verifySummaries.AddRange(await ExecutePreparedProjectScaffoldVerificationAsync(
                projectScaffoldPlan,
                toolRegistry,
                enforcer,
                callbacks,
                workspaceRoot,
                workMode,
                fileChanges,
                executedCommands,
                replayEntries,
                editFailureTracker,
                ct));
        }

        return BuildProjectScaffoldExecutionSummary(createSummary, verifySummaries);
    }

    private static bool ShouldRunProjectScaffoldVerificationFallback(
        ProjectScaffoldPlanningResult projectScaffoldPlan,
        IReadOnlyList<FileChangeRecord> fileChanges,
        IReadOnlyList<ToolReplayEntry> replayEntries,
        AgentWorkMode workMode)
    {
        return workMode != AgentWorkMode.Readonly &&
               HasProceedableProjectScaffoldPlan(projectScaffoldPlan) &&
               projectScaffoldPlan.Plan?.VerificationCommands.Count > 0 &&
               fileChanges.Count > 0 &&
               !replayEntries.Any(entry => string.Equals(entry.ToolName, "verify_project_scaffold", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<ProjectScaffoldToolSummary>> ExecutePreparedProjectScaffoldVerificationAsync(
        ProjectScaffoldPlanningResult projectScaffoldPlan,
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
        if (!HasProceedableProjectScaffoldPlan(projectScaffoldPlan))
        {
            return [];
        }

        var commands = GetApprovedProjectScaffoldVerificationCommands(projectScaffoldPlan);
        if (commands.Count == 0)
        {
            return [];
        }

        callbacks?.OnRunStep?.Invoke(
            AgentRunState.Verifying,
            "Auto scaffold verification",
            commands.Count == 1
                ? "AgentQ is running the approved scaffold verification command after deterministic project creation."
                : $"AgentQ is running {commands.Count} approved scaffold verification commands after deterministic project creation.");

        var summaries = new List<ProjectScaffoldToolSummary>();
        foreach (var command in commands)
        {
            var verifyResults = await ExecuteToolsAsync(
                [
                    ChatContent.CreateToolUse(
                        "auto_verify_project_scaffold_" + Guid.NewGuid().ToString("N"),
                        "verify_project_scaffold",
                        BuildProjectScaffoldVerifyInputJson(projectScaffoldPlan, command))
                ],
                toolRegistry,
                enforcer,
                callbacks,
                workspaceRoot,
                workMode,
                fileChanges,
                executedCommands,
                replayEntries,
                editFailureTracker,
                ct);

            summaries.Add(ProjectScaffoldToolSummary.Parse(verifyResults.FirstOrDefault()?.ToolResult));
        }

        return summaries;
    }

    private static string BuildProjectScaffoldCreateInputJson(ProjectScaffoldPlanningResult projectScaffoldPlan)
    {
        return JsonSerializer.Serialize(new
        {
            planId = projectScaffoldPlan.PlanId,
            planHash = projectScaffoldPlan.PlanHash,
            intent = projectScaffoldPlan.Intent,
            plan = projectScaffoldPlan.Plan,
            overwriteExistingFiles = false
        });
    }

    private static string BuildProjectScaffoldVerifyInputJson(ProjectScaffoldPlanningResult projectScaffoldPlan, string? command = null)
    {
        return JsonSerializer.Serialize(new
        {
            planId = projectScaffoldPlan.PlanId,
            planHash = projectScaffoldPlan.PlanHash,
            intent = projectScaffoldPlan.Intent,
            plan = projectScaffoldPlan.Plan,
            command
        });
    }

    private static IReadOnlyList<string> GetApprovedProjectScaffoldVerificationCommands(ProjectScaffoldPlanningResult projectScaffoldPlan) =>
        projectScaffoldPlan.Plan?.VerificationCommands
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Select(command => command.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

    private static string BuildProjectScaffoldExecutionSummary(
        ProjectScaffoldToolSummary createSummary,
        IReadOnlyList<ProjectScaffoldToolSummary> verifySummaries)
    {
        var builder = new StringBuilder();
        if (createSummary.Succeeded)
        {
            builder.AppendLine(createSummary.SkippedFiles.Count > 0
                ? "Prepared project scaffold was partially created."
                : "Prepared project scaffold was created.");
        }
        else
        {
            builder.AppendLine("Prepared project scaffold was not created.");
        }

        if (createSummary.CreatedFiles.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Created files:");
            foreach (var file in createSummary.CreatedFiles)
            {
                builder.AppendLine($"- {file}");
            }
        }

        if (createSummary.SkippedFiles.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Skipped files:");
            foreach (var file in createSummary.SkippedFiles)
            {
                builder.AppendLine($"- {file}");
            }
        }

        if (createSummary.Issues.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Issues:");
            foreach (var issue in createSummary.Issues)
            {
                builder.AppendLine($"- {issue}");
            }
        }

        if (verifySummaries.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Verification:");
            foreach (var verifySummary in verifySummaries)
            {
                builder.AppendLine(verifySummary.Succeeded
                    ? $"- Passed: {verifySummary.Command}"
                    : $"- Failed: {verifySummary.Command}");
            }
        }

        return builder.ToString().TrimEnd();
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
        ProjectScaffoldPlanningResult projectScaffoldPlan,
        IReadOnlyList<AgentQSystemSkill> selectedSystemSkills,
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
        var projectScaffoldPlanContext = ProjectScaffoldPlanner.BuildPlanContext(projectScaffoldPlan);
        var systemSkillContext = _systemSkillService.BuildContext(selectedSystemSkills);
        var taskContract = UserIntentTranslator.Translate(userText);
        var taskContractContext = TaskContractPromptBuilder.BuildContext(taskContract);
        var executionLessons = await _executionLessonMemoryService.TouchRelevantAsync(workspaceRoot, userText, taskContract, ct);
        var executionLessonContext = _executionLessonMemoryService.BuildContext(executionLessons);

        if (string.IsNullOrWhiteSpace(workspaceContext) &&
            string.IsNullOrWhiteSpace(linkedContext) &&
            string.IsNullOrWhiteSpace(memoryContext) &&
            string.IsNullOrWhiteSpace(mcpContext) &&
            string.IsNullOrWhiteSpace(linkStatusContext) &&
            string.IsNullOrWhiteSpace(explicitStackContext) &&
            string.IsNullOrWhiteSpace(taskContractContext) &&
            string.IsNullOrWhiteSpace(executionLessonContext) &&
            string.IsNullOrWhiteSpace(systemSkillContext) &&
            string.IsNullOrWhiteSpace(projectScaffoldPlanContext) &&
            string.IsNullOrWhiteSpace(scaffoldDecisionContext))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("The desktop app attached local context for this request only.");
        builder.AppendLine("Use this as supplemental runtime context; do not tell the user you lack previous conversation memory unless they explicitly ask about memory.");
        builder.AppendLine("Answer the latest user request directly before mentioning workspace inspection, session state, or missing context.");
        builder.AppendLine("Use the workspace snapshot for repository questions, but say when a file may be missing from the snapshot.");
        builder.AppendLine($"Current AgentQ work mode: {config.DesktopWorkMode}.");
        builder.AppendLine($"Current task profile: {taskProfile.Label}.");
        builder.AppendLine(taskProfile.ContextHint);
        builder.AppendLine(DesktopExecutionStrategyCatalog.ForProfile(taskProfile).FormatForPrompt());
        builder.AppendLine("Codebase discovery hint: use hybrid_search first when you need ranked candidate files with reasons.");
        builder.AppendLine("Code navigation hint: use symbol_search for known or likely identifiers before broad grep; then read_file the best candidate.");
        builder.AppendLine("Search fallback order: list_directory for folder structure and empty-workspace checks, symbol_search for definitions, semantic_search for meaning-based context when enabled, grep_search/glob_search for broad fallback.");
        builder.AppendLine("Evidence-backed analysis rule: when answering project analysis or documentation questions, cite the inspected files or commands in a short Evidence section and put unsupported inferences under Needs verification.");
        if (hasLinkIntent || !string.IsNullOrWhiteSpace(linkedContext) || !string.IsNullOrWhiteSpace(linkStatusContext))
        {
            builder.AppendLine("Link capability rule: AgentQ Desktop can attempt to fetch HTTP/HTTPS URLs when link auto-read is enabled. Never say AgentQ cannot access URLs categorically.");
        }

        if (!string.IsNullOrWhiteSpace(taskContractContext))
        {
            builder.AppendLine();
            builder.AppendLine(taskContractContext);
        }

        if (!string.IsNullOrWhiteSpace(executionLessonContext))
        {
            builder.AppendLine();
            builder.AppendLine(executionLessonContext);
        }

        if (!string.IsNullOrWhiteSpace(systemSkillContext))
        {
            builder.AppendLine();
            builder.AppendLine("Skill active: tool use required for file-producing tasks. Do not write file contents directly in raw response code blocks; use workspace/scaffold tools instead.");
            builder.AppendLine(systemSkillContext);
        }

        if (!string.IsNullOrWhiteSpace(explicitStackContext))
        {
            builder.AppendLine(explicitStackContext);
        }

        if (!string.IsNullOrWhiteSpace(projectScaffoldPlanContext))
        {
            builder.AppendLine();
            builder.AppendLine(projectScaffoldPlanContext);
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
            else if (attachment.IsTextDocument)
            {
                content.Add(ChatContent.CreateText(await BuildTextAttachmentContentAsync(attachment, ct)));
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

    private static async Task<string> BuildTextAttachmentContentAsync(DesktopAttachment attachment, CancellationToken ct)
    {
        try
        {
            var text = await File.ReadAllTextAsync(attachment.Path, Encoding.UTF8, ct);
            var wasTruncated = text.Length > MaxTextAttachmentChars;
            var preview = wasTruncated ? text[..MaxTextAttachmentChars] : text;
            var suffix = wasTruncated
                ? $"{Environment.NewLine}[attachment truncated after {MaxTextAttachmentChars} characters]"
                : string.Empty;
            return
                $"Attached document: {attachment.FileName} ({attachment.MediaType}){Environment.NewLine}" +
                "```text" + Environment.NewLine +
                preview +
                suffix + Environment.NewLine +
                "```";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return $"Attached document could not be read: {attachment.FileName}. Error: {ex.Message}";
        }
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
        return CreateProvider(config, callbacks: null);
    }

    private ILlmProvider CreateProvider(ProviderConfiguration config, DesktopToolCallbacks? callbacks)
    {
        ILlmProvider provider = config.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), ResolveModel(config, "gpt-4o")),
            "opencode-go" => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), ResolveModel(config, "gpt-4o"), name: "opencode-go"),
            "anthropic" => new AnthropicProvider(CreateAnthropicClient(config.BaseUrl), config.ApiKey),
            _ => new OpenAiCompatibleProvider(CreateOpenAiClient(config.BaseUrl, config.ApiKey), ResolveModel(config, "gpt-4o"), name: config.Provider)
        };

        return new ResilientLlmProvider(provider, onRetry: retry =>
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.Generating,
                $"Provider retry {retry.Attempt}/{retry.MaxRetries}",
                FormatProviderRetryDetail(retry)));
    }

    private static string FormatProviderRetryDetail(LlmProviderRetryInfo retry)
    {
        var status = retry.StatusCode == null ? "network/timeout" : $"HTTP {(int)retry.StatusCode} {retry.StatusCode}";
        var delay = retry.Delay == TimeSpan.Zero
            ? "immediately"
            : $"after {retry.Delay.TotalSeconds:0.#}s";
        return $"{retry.ProviderName} retrying {delay} because {status}: {DesktopPromptBuilder.Truncate(retry.ErrorMessage, 160)}";
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
        CancellationToken ct,
        TurnIntentClassification? turnIntent = null)
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
                if (turnIntent?.Type == TurnIntentType.Conversation &&
                    TurnIntentClassifier.IsStateChangingTool(tool.Name))
                {
                    var blockedInputJson = JsonSerializer.Serialize(parsedInput, new JsonSerializerOptions { WriteIndented = true });
                    var blockedMessage =
                        $"Turn intent is Conversation, so AgentQ blocked state-changing tool '{tool.Name}' before permission. " +
                        "Answer the user in prose, or ask for explicit execution if the user wants AgentQ to act.";
                    callbacks?.OnRunStep?.Invoke(AgentRunState.Failed, "Turn intent guard", blockedMessage);
                    callbacks?.OnToolError?.Invoke(tool.Name, blockedMessage);
                    replayEntries.Add(CreateReplayEntry(tool.Name, toolId, blockedInputJson, blockedMessage, isError: true, DateTime.UtcNow));
                    results.Add(ChatContent.CreateToolResult(toolId, blockedMessage, true));
                    continue;
                }

                if (ShouldStopRepeatedReadOnlyToolCall(tool.Name, parsedInput, editFailureTracker, out var loopMessage))
                {
                    var loopInputJson = JsonSerializer.Serialize(parsedInput, new JsonSerializerOptions { WriteIndented = true });
                    callbacks?.OnRunStep?.Invoke(AgentRunState.Failed, "Read-only tool loop guard", loopMessage);
                    callbacks?.OnToolError?.Invoke(tool.Name, loopMessage);
                    replayEntries.Add(CreateReplayEntry(tool.Name, toolId, loopInputJson, loopMessage, isError: true, DateTime.UtcNow));
                    results.Add(ChatContent.CreateToolResult(toolId, loopMessage, true));
                    continue;
                }

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
                    !await RequestToolPermissionAsync(tool, inputJson, workMode, workspaceRoot, enforcer, callbacks))
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

                        await RecordProjectScaffoldFileChangesAsync(
                            tool.Name,
                            result.Content,
                            workspaceRoot,
                            fileChanges,
                            callbacks,
                            ct);
                    }

                    results.Add(ChatContent.CreateToolResult(
                        toolId,
                        TruncateToolResult(result.Content, workspaceRoot, out var wasTruncated, out var savedToolOutputPath),
                        result.IsError));

                    if (wasTruncated)
                    {
                        var savedMessage = string.IsNullOrWhiteSpace(savedToolOutputPath)
                            ? "Full output could not be saved."
                            : $"Full output saved to {savedToolOutputPath}.";
                        callbacks?.OnToolOutput?.Invoke(tool.Name, $"Tool result was truncated to {MaxToolResultChars} chars before being sent back to the model. {savedMessage}");
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

    private static bool ShouldStopRepeatedReadOnlyToolCall(
        string toolName,
        Dictionary<string, object?> input,
        Dictionary<string, int> toolLoopTracker,
        out string loopMessage)
    {
        loopMessage = string.Empty;
        if (!IsReadOnlyRepeatGuardTool(toolName))
        {
            return false;
        }

        var key = BuildToolRepeatKey(toolName, input);
        var count = toolLoopTracker.TryGetValue(key, out var existing) ? existing + 1 : 1;
        toolLoopTracker[key] = count;
        if (count < RepeatedReadOnlyToolLimit)
        {
            return false;
        }

        loopMessage = $"Repeated read-only tool call detected for {toolName} with the same input {count} times. " +
                      "Stop repeating this lookup; use the existing result, inspect a different path or query, or move to the next action.";
        return true;
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

    private static string BuildToolRepeatKey(string toolName, Dictionary<string, object?> input)
    {
        var orderedInput = input
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
        return $"readonly-repeat|{toolName}|{JsonSerializer.Serialize(orderedInput)}";
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
            InputJson = SensitiveTextRedactor.Redact(TrimReplayText(inputJson, 8000)),
            ResultPreview = SensitiveTextRedactor.Redact(TrimReplayText(result, 8000)),
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
        if (!TryGetTrackedCommand(toolName, input, out var command))
        {
            return;
        }

        executedCommands.Add(command);
    }

    private static bool TryGetTrackedCommand(
        string toolName,
        IReadOnlyDictionary<string, object?> input,
        out string command)
    {
        command = string.Empty;
        if (string.Equals(toolName, "bash", StringComparison.Ordinal))
        {
            return TryGetString(input, "command", out command);
        }

        if (!string.Equals(toolName, "verify_project_scaffold", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryGetString(input, "command", out command))
        {
            return true;
        }

        if (input.TryGetValue("plan", out var rawPlan) &&
            TryGetFirstVerificationCommand(rawPlan, out command))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetFirstVerificationCommand(object? rawPlan, out string command)
    {
        command = string.Empty;
        if (rawPlan == null)
        {
            return false;
        }

        if (rawPlan is ProjectScaffoldPlanModel plan)
        {
            command = plan.VerificationCommands.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(command);
        }

        if (rawPlan is JsonElement element &&
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("verificationCommands", out var commandsElement) &&
            commandsElement.ValueKind == JsonValueKind.Array)
        {
            command = commandsElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(command);
        }

        try
        {
            var json = JsonSerializer.Serialize(rawPlan);
            using var document = JsonDocument.Parse(json);
            return TryGetFirstVerificationCommand(document.RootElement, out command);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<bool> RequestToolPermissionAsync(
        ITool tool,
        string inputJson,
        AgentWorkMode workMode,
        string workspaceRoot,
        IPermissionEnforcer enforcer,
        DesktopToolCallbacks? callbacks)
    {
        var policy = ToolPermissionPolicy.Evaluate(tool.Name, inputJson, workspaceRoot, workMode);
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

    private async Task RecordProjectScaffoldFileChangesAsync(
        string toolName,
        string toolResult,
        string workspaceRoot,
        List<FileChangeRecord> fileChanges,
        DesktopToolCallbacks? callbacks,
        CancellationToken ct)
    {
        if (!string.Equals(toolName, "create_project_scaffold", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var relativePath in ExtractProjectScaffoldCreatedFiles(toolResult))
        {
            if (!TryResolveWorkspaceFile(relativePath, workspaceRoot, out var fullPath) ||
                !File.Exists(fullPath) ||
                Directory.Exists(fullPath))
            {
                continue;
            }

            var change = await BuildFileChangeRecordAsync(new FileSnapshot(fullPath, false, string.Empty), workspaceRoot, ct);
            if (change == null)
            {
                continue;
            }

            fileChanges.Add(change);
            callbacks?.OnRunStep?.Invoke(
                AgentRunState.RecordingChanges,
                "Evidence: file changed",
                $"{change.RelativePath} ({change.Summary})");
            callbacks?.OnFileChanged?.Invoke(change);
        }
    }

    private static IReadOnlyList<string> ExtractProjectScaffoldCreatedFiles(string toolResult)
    {
        if (string.IsNullOrWhiteSpace(toolResult))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(toolResult);
            if (!document.RootElement.TryGetProperty("createdFiles", out var createdFiles) ||
                createdFiles.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return createdFiles.EnumerateArray()
                .Where(file => file.ValueKind == JsonValueKind.String)
                .Select(file => file.GetString())
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Select(file => file!)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsFileMutationTool(string toolName)
    {
        return toolName is "write_file" or "edit_file";
    }

    private static bool IsReadOnlyRepeatGuardTool(string toolName)
    {
        return string.Equals(toolName, "list_directory", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "read_file", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "glob_search", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "grep_search", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "symbol_search", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "semantic_search", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, "hybrid_search", StringComparison.OrdinalIgnoreCase);
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

    public static string TruncateToolResult(string value, string workspaceRoot, out bool wasTruncated, out string? savedPath)
    {
        savedPath = null;
        if (value.Length <= MaxToolResultChars)
        {
            wasTruncated = false;
            return value;
        }

        wasTruncated = true;
        savedPath = TrySaveFullToolOutput(value, workspaceRoot);
        var hint = string.IsNullOrWhiteSpace(savedPath)
            ? "Full output could not be saved."
            : $"Full output saved to: {savedPath}{Environment.NewLine}Use grep_search or read_file with offset/limit to inspect the saved output instead of rerunning the command blindly.";
        return value[..MaxToolResultChars] +
               Environment.NewLine +
               "[tool result truncated]" +
               Environment.NewLine +
               hint;
    }

    private static string? TrySaveFullToolOutput(string value, string workspaceRoot)
    {
        try
        {
            var root = string.IsNullOrWhiteSpace(workspaceRoot) ? Environment.CurrentDirectory : workspaceRoot;
            var directory = Path.Combine(root, ".agentq", ToolOutputDirectoryName);
            Directory.CreateDirectory(directory);
            var fileName = $"tool_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.txt";
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, value, Encoding.UTF8);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private ToolRegistry CreateToolRegistry(ProviderConfiguration config, string workspaceRoot)
    {
        var registry = new ToolRegistry();
        registry.Register(new BashTool());
        registry.Register(new ListDirectoryTool());
        registry.Register(new ReadFileTool());
        registry.Register(new WriteFileTool());
        registry.Register(new EditFileTool());
        registry.Register(new GrepTool());
        registry.Register(new GlobTool());
        registry.Register(new DesktopProjectScaffoldPlanTool(workspaceRoot, _projectScaffoldPlanner, _projectScaffoldPlanRegistry));
        registry.Register(new DesktopProjectScaffoldCreateTool(workspaceRoot, planRegistry: _projectScaffoldPlanRegistry));
        registry.Register(new DesktopProjectScaffoldVerifyTool(workspaceRoot, planRegistry: _projectScaffoldPlanRegistry));
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
                registry.TryRegister(new McpBridgeTool(
                    McpToolName.Build(server.Name, tool.Name),
                    server,
                    tool,
                    client));
            }
        }
    }
}

internal sealed record DesktopAssistantTurn(
    string AssistantText,
    List<ChatContent> AssistantContent,
    List<ChatContent> ToolUses);

internal sealed record ProjectScaffoldToolSummary(
    bool Succeeded,
    string Command,
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> SkippedFiles,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> VerificationCommands)
{
    public static ProjectScaffoldToolSummary Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty(succeeded: false);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new ProjectScaffoldToolSummary(
                Succeeded: TryGetBool(root, "succeeded"),
                Command: TryGetString(root, "command"),
                CreatedFiles: TryGetStringArray(root, "createdFiles"),
                SkippedFiles: TryGetStringArray(root, "skippedFiles"),
                Issues: TryGetStringArray(root, "issues"),
                VerificationCommands: TryGetStringArray(root, "verificationCommands"));
        }
        catch
        {
            return new ProjectScaffoldToolSummary(
                Succeeded: false,
                Command: string.Empty,
                CreatedFiles: [],
                SkippedFiles: [],
                Issues: [DesktopPromptBuilder.Truncate(json, 400)],
                VerificationCommands: []);
        }
    }

    private static ProjectScaffoldToolSummary Empty(bool succeeded) =>
        new(
            Succeeded: succeeded,
            Command: string.Empty,
            CreatedFiles: [],
            SkippedFiles: [],
            Issues: [],
            VerificationCommands: []);

    private static bool TryGetBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static string TryGetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static IReadOnlyList<string> TryGetStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }
}

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
