# Agent Q Architecture Notes

> Updated: 2026-06-12  
> Scope: C#/.NET desktop coding agent runtime

Agent Q is a desktop agent runtime for real development work. It is not intended to be a thin chat UI around an LLM.

The model is used for understanding intent, reasoning, code generation, explanations, and repair suggestions. Actions that must reliably happen are owned by deterministic Desktop services: scaffold execution, local server start/stop, file mutation tracking, verification, recovery, and run-state reporting.

## Core Principles

1. Deterministic execution first

   Greenfield scaffold creation, local server lifecycle, verification workflows, file mutation snapshots, and recovery must not depend on whether the model happened to call the right tool.

2. Latest user request wins

   Past conversation, session summaries, checkpoints, execution lessons, scaffold hints, verification hints, logs, and pasted examples are historical evidence. They must not override the newest user request.

3. Conversation and action are separate

   Conversation turns answer with explanation or advice. They must not trigger file writes, shell commands, scaffold execution, installs, commits, or local server actions. Action and hybrid turns require concrete targets, policy checks, approvals, deterministic execution, and verification where appropriate.

4. No hidden fallback

   Fallback never means silently creating files. Scaffold execution requires an approved plan, valid `planId` and `planHash`, validated workspace path, and respected overwrite rules.

5. Completion requires evidence

   Agent Q must not treat "done" prose as completion when the requested mutation, command, verification, scaffold, or server action did not actually run. Tool replay, file-change records, verification results, and task-contract evidence are the authoritative sources.

6. Lessons are behavior rules

   Execution lesson memory stores compact failure/success rules, not long conversations or private content. It should reduce repeated execution mistakes without becoming another source of stale user intent.

## Project Layout

```text
csharp/
  AgentQ.Api                  Shared DTOs, content blocks, stream/tool contracts
  AgentQ.Core                 Provider abstractions, configuration, chat models
  AgentQ.Providers.OpenAi     OpenAI-compatible provider implementation
  AgentQ.Providers.Anthropic  Anthropic provider implementation
  AgentQ.Tools                Shared deterministic tools
  AgentQ.Cli                  Interactive and non-interactive CLI runtime
  AgentQ.Desktop              WPF desktop runtime and orchestration services
  AgentQ.Tests                Unit and integration tests
```

## Desktop Runtime Flow

```text
User input
-> UserTurnUnderstanding
-> effective intent and task contract
-> deterministic primary path when applicable
   -> local server service
   -> scaffold executor
   -> verification workflow
-> otherwise provider stream + tool loop
-> file-change snapshots and tool replay
-> verification and confidence assessment
-> final-answer consistency guard
-> UI run summary and optional execution lessons
```

The transient context starts with a latest-request priority block. Memory, workspace snapshots, skills, scaffold recommendations, verification hints, and execution lessons are appended as supporting evidence only.

## Pending Plan Continuity

Agent Q may carry only one immediate execution-awaiting plan across turns.

```text
Assistant gives a concrete implementation/scaffold plan
-> assistant explicitly asks for immediate approval such as "진행해줘"
-> Desktop captures a short PendingExecutionPlan in memory
-> next user turn approves the immediately previous plan
-> routing text becomes the pending plan goal
-> normal intent, approval, deterministic execution, and evidence guards still apply
```

This is not long-term memory. It must not read old session summaries, checkpoints, RAG results, or execution lessons as approval. The pending plan expires after the next user turn, workspace changes, stale age, topic change, cancellation, or a new requirement that is not a direct approval.

## Safe Scaffold Mode

Safe Scaffold Mode is a primary deterministic path:

```text
User asks for a concrete new project
-> ScaffoldPlanner creates a plan
-> UI shows files and verification commands
-> user approves
-> DesktopScaffoldService / worker executor creates files
-> ScaffoldReady
-> ImplementationContract checks requested UI/features and forbidden placeholders
-> provider code loop continues implementation when required
-> build, preview, DOM, and visual evidence are required for frontend completion
-> file mutation snapshots are recorded
-> verification runs
-> model explains, repairs, or continues
```

If the model misses a scaffold tool call but an approved scaffold plan exists, the Desktop service is responsible for executing the plan through the deterministic executor. The model is not the source of truth for whether scaffold files were created.

Scaffold success is not task completion. Frontend greenfield runs create an implementation contract after scaffold execution. The contract rejects placeholder-only output such as `ShoppingCart is ready`, `Hello World`, `Vite + React`, `App is ready`, `Lorem ipsum`, or `TODO`. Domain-specific requests, such as a luxury clothing shop, must show matching evidence in source and runtime checks: product cards/catalog, prices, cart/bag actions, wishlist/save actions, hero/lookbook/editorial sections, and luxury visual language.

Concrete Korean greenfield requests such as `럭셔리 의류 쇼핑몰 만들어줘` are expected to route as `CreateProject` / shopping-cart scaffold requests. After deterministic scaffold creation, the provider implementation loop must update the real app files, including `src/App.jsx` and styling such as `src/styles.css`, before Agent Q can consider the task implemented.

The final-answer guard blocks completion when the implementation contract is still missing requirements. If source checks pass but frontend runtime evidence is absent, the guard also blocks completion until localhost preview, DOM, and screenshot/visual evidence exists.

`ImplementationRuntimePreviewService` provides the deterministic preview path: it starts or reuses the local server through `DesktopLocalServerService`, verifies that the localhost URL responds, then attempts workspace-local Playwright browser verification. When Playwright is available it captures desktop and mobile screenshots under `.agentq/preview/`, collects browser console/page errors, stores the browser-rendered DOM snapshot, and records `implementation_runtime_preview` replay evidence. Missing Playwright support, console errors, or screenshot visual findings are treated as failed preview evidence instead of completion evidence.

When runtime preview fails, `DesktopAgentService` returns the concrete preview evidence to the provider loop as a bounded repair instruction. The repair prompt includes the URL, command, local-server message, DOM snapshot, missing DOM evidence, console errors, visual findings, and screenshot artifacts. The loop tracks attempt count, failure signatures, and whether the last repair recorded file changes. If the same failure repeats, no files changed, or the attempt budget is exhausted, the final-answer guard reports the failed verification instead of looping indefinitely or claiming completion.

The same repair pattern applies to failed build/test/verification replay evidence. Failed shell/scaffold evidence is classified as build failure, test failure, scaffold verification failure, or generic verification failure and returned to the provider loop with a focused repair instruction. A frontend run is considered complete only when successful runtime preview replay evidence exists, not merely because a dev-server command was attempted.

Repair instructions are case-specific. Agent Q classifies missing npm scripts, missing dependencies, dev-server startup failures, React runtime errors, import/export mismatches, JSX syntax errors, blank or broken screens, missing DOM requirements, and mobile visual layout failures. Each strategy names priority files, preferred repair actions, actions to avoid, and verification commands to rerun. Package/script/dependency repairs still go through normal workspace validation, permission checks, and replay evidence; the model is guided to patch `package.json` or source files, then rerun install/build/preview through the permissioned tool path instead of silently treating edits as success.

## Local Server Mode

Local server requests use `TaskContract` and `DesktopLocalServerService`:

```text
User asks to start or stop a local server
-> UserIntentTranslator creates a RunLocalServer or StopLocalServer contract
-> DesktopLocalServerService starts, reuses, verifies, or stops the server
-> session state is stored under .agentq/local-server/session.json
-> UI displays URL/status and Open/Stop actions
```

The service prefers package scripts in this order: `dev`, `start`, `preview`. A reported server URL is success evidence only after the URL responds. Stale PID/session reuse must be rejected.

## Tool And Permission Model

Tools are deterministic execution units. They must validate workspace boundaries, avoid following unsafe symlink/reparse paths, return structured errors, and preserve evidence.

Important tool rules:

- `read_file` streams requested line windows and rejects binary/NUL files.
- `write_file` and `edit_file` preserve existing text encoding, reject binary targets, and write through same-directory temporary files.
- `grep_search` and `glob_search` avoid reparse traversal and cap large outputs.
- `list_directory` reports reparse entries without following target metadata.
- `web_search` requires permission, validates query size, and caps result text.
- `bash` requires permission, blocks dangerous command patterns, reports exit code/stdout/stderr, and treats output as untrusted evidence.

Desktop and CLI permission prompts should make shell, network, project write/edit/create/delete, and destructive risks visible. Session-reusable approval is intentionally narrow.

## Provider Boundary

Providers convert between Agent Q chat/tool content and provider-specific APIs. They must not forward malformed historical tool calls back to providers.

Key invariants:

- Blank or malformed tool-use IDs/names are not sent as valid provider tool calls.
- Tool arguments sent to OpenAI-compatible APIs are normalized to object JSON.
- Tool result messages are not kept without their corresponding assistant tool-use context after compaction.
- Streaming usage and tool deltas are merged into accurate final chunks.

## Context, Memory, And Lessons

Session summaries, checkpoints, project memory, execution lessons, and pasted logs are historical evidence. They are not fresh instructions.

Execution lessons live under:

```text
.agentq/lessons/execution-lessons.json
.agentq/lessons/execution-lesson-events.jsonl
```

They should store:

- intent type
- failure pattern
- correct next behavior
- confidence and success/failure counters

They should not store long conversations, private unrelated user content, secrets, or large tool outputs.

## Verification And Final Answers

Verification results, file mutation snapshots, tool replay, local-server state, and scaffold execution records determine whether a run can be reported as complete.

Final-answer guards must replace or block unsupported success claims when:

- requested file changes have no mutation evidence
- verification failed or was cancelled
- scaffold execution failed or did not create files
- scaffold succeeded but the implementation contract still has placeholders or missing requirements
- frontend implementation lacks localhost preview, DOM, or screenshot/visual evidence
- local server startup failed
- max tool steps or no-tool guard stopped the run
- the model claims completion without tool evidence

User-visible guard failures should be humanized. Internal terms such as `TaskContract`, `ModifyCode`, `CreateProject`, `RunVerification`, and "Please retry; AgentQ should ..." belong in diagnostics or retry prompts, not as the final text shown to the user.

## UI State

The UI should make run truth visible:

- failure and guard-stop states must not look green
- "not complete", "not saved", "not verified", and similar negated status text must not be styled as success
- draft input must not be overwritten by continue, resume, stop-server, checkpoint, or verification-fix actions
- busy state disables stale continue/resume actions
- project panel reset must clear stale Ready/green analysis state

## Current Refactor Direction

The desired direction is a shared turn-state boundary:

```text
Raw user text
-> TurnState
-> route decision
-> plan or answer
-> permission
-> execute
-> verify
-> final answer
```

After a turn state is created, downstream services should use that state instead of independently reinterpreting raw user text for execution decisions.

Current implementation status:

- `AgentTurnState` is now the per-turn routing boundary in the primary `DesktopAgentService.SendAsync` path.
- It carries raw user text, routing text, `UserTurnUnderstanding`, rule intent, effective intent, task profile, task contract, scaffold plan, selected skills, and context/tool/memory/verification/final-answer policies.
- Context assembly, routed user messages, task decomposition, direct local-server execution, direct scaffold execution, provider tool batches, and task-contract direct fallback now consume `AgentTurnState`.
- Worker execution receives `AgentTurnParentContext`, a compact parent trace/policy projection, so worker step prompts stay scoped to the parent turn instead of becoming a fresh raw-text authority.
- Scaffold, local server, verification fallback, worker execution, provider tool loop, and final-answer guards have been audited against TurnState policies and tool/service replay evidence.
- The final TurnState refactor was verified with focused routing/worker/context/verification tests, `AgentQ.Tests` build, `AgentQ.Desktop` build, and a full `AgentQ.Tests` run with 1120 passed / 0 failed.

## Audit Checklist

Preserve these guardrails:

- workspace path validation
- explicit approval before risky actions
- plan id/hash validation for scaffold execution
- permission checks for shell/server/network/write/delete/commit actions
- verification after scaffold or meaningful code changes
- file mutation snapshots before edits
- read-only loop detection
- task-contract completion checks
- final-answer consistency checks after changes
