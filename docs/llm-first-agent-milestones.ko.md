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

- false success -> task-contract retry -> actual tool call -> contract evidence
- latest user request priority before task contract and workspace context
- search-and-summarize through `web_search`
- compaction preserving priority context and evidence
- evidence-aware final answer checking

## Remaining Work

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
