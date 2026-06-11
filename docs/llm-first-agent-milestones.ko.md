# LLM-first Agent Milestones

## Purpose

Agent Q is moving toward an LLM-first desktop agent runtime.

The model should have stronger authority over intent understanding, planning, tool choice, and repair. Desktop should stay as a narrow runtime safety layer for workspace boundaries, destructive actions, approvals, evidence recording, and deterministic paths that must be reliable.

This does not remove safety. It changes the order of responsibility:

```text
Latest user request
-> LLM interprets intent and chooses the action path
-> Desktop enforces hard safety and records evidence
-> TaskContract checks whether the answer actually satisfied the request
-> Final answer must match tool evidence, file changes, or concrete limitations
```

## Required Core Design: User Turn Understanding

LLM-first must not mean "execute the first imperative sentence." The first semantic step should be a full user-turn understanding pass.

The model must separate:

- the user's primary intent
- embedded commands inside examples, quotes, logs, transcripts, bad responses, or test cases
- the action, if any, requested for this current turn
- content that should be treated as evidence rather than instructions
- clarification needs

Required pipeline:

```text
Raw user message
-> UserTurnUnderstanding
-> ExecutionDecision
-> TaskContract
-> Tool/service routing
-> Evidence-backed final answer
```

`UserTurnUnderstanding` should be richer than `Action | Conversation | Hybrid | Ambiguous`.

Suggested shape:

```json
{
  "primaryIntent": "MetaFeedback|Conversation|Action|Hybrid|Ambiguous",
  "userGoal": "",
  "embeddedContent": [
    {
      "kind": "example_user_request|bad_agent_response|log|quote|code|error|other",
      "text": "",
      "shouldExecute": false,
      "reason": ""
    }
  ],
  "actualRequestedAction": {
    "shouldExecute": false,
    "actionKind": "none|inspect|create|edit|delete|run|search|scaffold|server|git",
    "target": "",
    "reason": ""
  },
  "requiresReadOnlyInspection": false,
  "requiresWrite": false,
  "requiresShell": false,
  "requiresNetwork": false,
  "isConcreteEnough": false,
  "clarifyingQuestion": "",
  "confidence": 0.0
}
```

Hard rule for this design:

```text
Commands embedded inside examples, quoted text, logs, transcripts, or bad-agent-response demonstrations must not be executed unless the user explicitly asks Agent Q to execute that embedded command now.
```

Example:

```text
User message:
test2 폴더를 생성해줘
=====
저는 인공지능이라 실제로 독서나 게임을...

지금처럼 이딴 대답이 나오게 하는걸 못고치나?
```

Expected understanding:

```json
{
  "primaryIntent": "MetaFeedback",
  "userGoal": "Agent Q is giving an off-target answer for a simple folder creation request, and the user wants that failure mode fixed.",
  "embeddedContent": [
    {
      "kind": "example_user_request",
      "text": "test2 폴더를 생성해줘",
      "shouldExecute": false,
      "reason": "This is the example request that Agent Q failed to handle."
    },
    {
      "kind": "bad_agent_response",
      "text": "저는 인공지능이라 실제로 독서나 게임을...",
      "shouldExecute": false,
      "reason": "This is the bad response being criticized."
    }
  ],
  "actualRequestedAction": {
    "shouldExecute": false,
    "actionKind": "none",
    "target": "",
    "reason": "The current turn asks about Agent Q behavior, not folder creation."
  }
}
```

This layer should become the source of truth for LLM-first routing. `TurnIntentClassification`, `DesktopTaskProfile`, scaffold detection, and task contracts should consume this understanding instead of independently guessing from raw text.

## Current Implemented State

### M1. LLM-first routing in provider-backed runs

Status: implemented.

- The model is treated as the primary semantic judge when a provider endpoint is configured.
- Desktop direct primary paths are reduced in provider-backed runs.
- Desktop remains responsible for hard safety, permission policy, workspace validation, and evidence recording.
- Conversation turns no longer blanket-block safe read/search tools.

### M2. TaskContract expansion

Status: implemented.

Current contract intents include:

- `RunLocalServer`
- `StopLocalServer`
- `DeletePath`
- `CreateDirectory`
- `CreateFile`
- `CreateProject`
- `ModifyCode`
- `RunVerification`
- `SearchAndSummarize`
- `InspectProject`

The contract prompt now includes required actions, done conditions, invalid completions, and required completion evidence.

### M3. Low-friction tool execution for common safe actions

Status: implemented.

- `create_directory` exists as a low-risk workspace action.
- Empty/new file creation can flow through `write_file`.
- Coding mode allows verification commands without approval when classified as verification.
- Coding mode allows read-only public `web_search` for evidence gathering.

### M4. Web search evidence path

Status: implemented.

- `web_search` is registered in the Desktop tool registry.
- `SearchAndSummarize` requires `web_search`, `fetch_url`, or other read/search evidence.
- Permission policy treats `web_search` as read-only network evidence in Coding mode.

### M5. Latest-request priority context

Status: implemented.

Transient context now starts with `Latest user request priority`.

This makes the latest user request and active TaskContract the routing anchor. Workspace context, memory, scaffold hints, skills, and execution lessons are explicitly supplemental and must not replace the latest request.

### M6. Conversation compaction repair

Status: implemented.

- Broken/non-readable tool summary markers were replaced with structured ASCII summaries.
- Tool use summaries preserve tool name, tool id, and compact input.
- Tool result summaries preserve status, preview, and important evidence.
- Long text compaction preserves priority lines such as latest request, current task contract, required evidence, verification, errors, and next action.

### M7. Evidence-aware final answer guard

Status: implemented.

`TaskContractCompletionChecker` now has replay-evidence-aware overloads.

Examples:

- `CreateDirectory` cannot be completed by merely saying "created" unless `create_directory` evidence exists.
- `SearchAndSummarize` cannot be completed by invented source wording unless `web_search`, `fetch_url`, or read/search evidence exists.
- `RunVerification` requires an executed command or shell evidence.

### M8. LLM-first regression tests

Status: implemented.

Focused regression coverage now includes:

- false success -> task-contract direct fallback or retry -> actual tool call -> contract evidence
- latest user request priority before task contract and workspace context
- search-and-summarize through `web_search`
- compaction preserving priority context and evidence
- evidence-aware final answer checking

## Remaining Work

### R0. Implement UserTurnUnderstanding as the routing source of truth

Status: implemented for the primary DesktopAgentService path, with broader real-provider smoke testing still recommended.

Current implementation:

- Added `UserTurnUnderstanding`, `EmbeddedContentItem`, and `ExecutionDecision`.
- Added provider-backed UserTurnUnderstanding JSON classification as the first semantic model judgment when a provider is configured.
- Kept the previous legacy `{ type, actionKind, ... }` intent JSON shape as backward-compatible input for tests and older providers.
- `DesktopAgentService` now creates a turn-understanding object before task profile, turn intent, scaffold planning, skill selection, local lessons, task decomposition, and task-contract translation.
- Most routing consumers now use sanitized `routingText` instead of raw `userText`.
- If the turn is understood as `MetaFeedback`, the embedded command is preserved as evidence and task-contract execution is disabled.
- If `UserTurnUnderstanding` marks the turn as non-executing evidence, it overrides later LLM/rule intent back to `Conversation`.
- If the model incorrectly marks a clear direct action as non-executing, deterministic fallback preserves the explicit action contract.
- `Conversation` turns now block workspace write, shell, verification, scaffold, git, external-write, and destructive tool calls even if the model emits a tool call.
- Added diagnostics for primary intent, embedded content count/kinds, execution decision, reason, and sanitized routing text.
- Added regression coverage for:
  - pasted example command plus bad Agent Q answer complaint does not execute the embedded command
  - pasted example command is still blocked when the LLM classifier incorrectly returns `Action` and the model emits a write tool call
  - quoted/log command evidence does not execute when the user asks for analysis
  - direct `test2 폴더를 생성해줘` still executes as the current action
  - model prose failure can be repaired by direct task-contract fallback for folder create/delete

Remaining:

- Run real-provider smoke tests in the Desktop UI with Korean prompts, especially mixed examples/logs and direct folder/file commands.
- Add broader log/error-command non-execution tests beyond the quoted/log analysis regression now covered.
- Revisit scaffold planning quality for broad greenfield prompts; this is related but separate from the embedded-command leak.

Build the explicit understanding layer described above and route LLM-first execution through it.

Tasks:

- Add `UserTurnUnderstanding`, `EmbeddedContentItem`, and `ExecutionDecision` models.
- Replace direct raw-text routing in LLM-first mode with:

```text
raw user text -> UserTurnUnderstanding -> ExecutionDecision -> TaskContract
```

- Ensure embedded commands are treated as examples/evidence unless `actualRequestedAction.shouldExecute` is true.
- Update `TurnIntentClassification`, `DesktopTaskProfile`, scaffold detection, and task contract translation to consume the understanding object where possible.
- Add diagnostics showing:
  - primary intent
  - embedded content count and kinds
  - execution decision
  - reason for not executing embedded commands
- Add regression tests for:
  - example command plus bad answer complaint must not execute the example command
  - quoted command must not execute unless explicitly re-requested
  - log/error text containing commands must not execute
  - simple direct command still executes when it is the primary user intent

Done when:

- A turn like `test2 폴더를 생성해줘` followed by a pasted bad Agent Q response is classified as `MetaFeedback`.
- The embedded folder creation request is preserved as evidence but not executed.
- Direct `test2 폴더를 생성해줘` without meta-feedback context still routes as an actionable create-directory request.

### R1. Broader regression pass

Run a wider focused suite around Desktop service, permission policy, task contracts, compaction, and tool execution.

Suggested command:

```powershell
dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore --filter "DesktopAgentService|TaskContract|ToolPermission|DesktopConversationCompactorTests" --logger "console;verbosity=minimal"
```

### R2. Full build stability

Use non-incremental Desktop builds when WPF `.baml` incremental artifacts are stale.

```powershell
dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore --no-incremental
```

### R3. Real provider smoke tests

Use a configured provider to manually verify:

- "logs 폴더 만들어줘"
- "notes.md 파일 하나 만들어줘"
- "테스트 돌려줘"
- "트리노드 후기 찾아서 정리해줘"
- "이 방향 괜찮을까?"

Expected behavior:

- concrete action requests use tools
- search requests gather evidence first
- consultation can inspect when useful
- final answers do not claim success without evidence

### R4. Gradual Desktop guard strengthening

After real usage, add only evidence-backed rules:

- repeated user corrections
- repeated false-success patterns
- repeated missing-tool patterns
- repeated unsafe target attempts

Rules should stay reviewable and should not silently override the LLM unless they protect hard safety or a proven contract failure.
