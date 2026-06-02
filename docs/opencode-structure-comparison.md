# opencode structure comparison

This note records the completed comparison between `C:\Users\admin\Desktop\opencode-dev\packages\opencode\src` and Agent-Q Desktop.

## Reflected structures

- Scaffold decision flow: Agent-Q now treats worker scaffolds as optional references and asks focused questions for underspecified new-project requests.
- Clarification state: pending project questions are preserved in run state and saved session summaries.
- Task tracking: coding work prompts include checklist/status guidance and named plan item statuses.
- Provider retry policy: retryable provider failures are limited to network, timeout, 408, 429, and 5xx cases.
- Provider retry visibility: retry attempts are surfaced in the Desktop run timeline.
- Provider failure classification: auth, rate limit, service, and output/context-length failures are reported with distinct user-facing messages.
- Compaction context: important tool-output lines, paths, commands, failures, and exit codes are preserved during conversation compaction.
- Tool output truncation: long Desktop tool outputs are saved under `.agentq/tool-output/` and the model receives the saved path plus inspection guidance.
- Shell command arity: permission summaries label long shell commands with their human-readable command prefix while preserving the full command.
- Tool registry safety: duplicate tool registrations are blocked or recorded, and skipped duplicate tools are reported in Desktop/CLI capability snapshots.
- Tool descriptions and routing: read-before-edit/write, search routing, evidence-backed analysis, link handling, and final reporting rules are represented in Desktop prompt assembly.
- Permission policy: destructive shell/Git recovery commands are blocked; reusable per-run approval exists for workspace writes and verification commands.
- File mutation snapshots and revert: Agent-Q has per-file mutation snapshots and file-change review revert, which covers the practical Desktop subset of opencode's broader session revert/snapshot system.

## Covered by existing Agent-Q systems

- LSP/code intelligence: Agent-Q uses symbol search, hybrid search, Roslyn C# analysis, TypeScript/Python/native workers, and dependency graphs rather than a full LSP server tool.
- Session run state: Desktop uses active cancellation tokens, `IsBusy`, run timeline states, checkpoint/session-summary resume, and verification retry plans.
- Background/subagent work: Agent-Q has task decomposition, multi-agent role planning, and worker execution scaffolds, but does not currently expose opencode-style background subagent sessions.
- Project/worktree detection: Agent-Q uses workspace analysis, Git branch status, Git recovery analyzers, and selected workspace roots rather than opencode's multi-instance/worktree runtime.
- Storage/config: Agent-Q uses Desktop config, project config, memory, checkpoints, tool replay, and `.agentq` artifacts rather than opencode's storage layer.
- MCP: Agent-Q supports configured stdio MCP servers through bridge tools; duplicate bridge names are now guarded.
- Link/web fetch: Agent-Q Desktop has link auto-read and explicit fetch-result prompt context instead of opencode's webfetch/websearch tools.

## Deliberately not ported

- opencode HTTP server/API, ACP, TUI, account/auth, sync/share, control-plane, installer, and remote reference systems: these are product-surface or hosted-service features outside the current Windows Desktop coding-assistant scope.
- Full plugin/skill runtime: Agent-Q currently has native tools, MCP bridge tools, project memory, and worker scaffolds. A full dynamic plugin/skill system would be a separate product feature, not a small safety or behavior fix.
- Full LSP server orchestration: useful but larger than the current desktop repair target; Agent-Q's current static and worker-based code intelligence already covers the immediate project-analysis failures.
- Full session revert by message/part: Agent-Q's file mutation snapshots and review-panel revert cover the local file safety use case without introducing opencode's full message graph and storage model.
- Background subagent sessions: valuable future work, but it requires durable job state, UI affordances, cancellation, and result injection. Current work kept the safer local role/checklist and worker execution path.

## Verification

The reflected changes were verified through the Agent-Q test suite after each implementation round. The latest full run after this comparison should be:

```powershell
dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj
```

