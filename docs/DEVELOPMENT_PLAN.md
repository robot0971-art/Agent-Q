# Agent Q Development Plan

## Goal

Agent Q should behave like a reliable desktop coding agent runtime, not a loose chat wrapper.

The immediate priority is to reduce off-target answers and accidental execution by making every turn pass through one explicit state object before any planning, tool call, scaffold, memory update, or final answer.

Target shape:

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

## Current Problem

Several services can still interpret the same raw user text independently.

This causes failures such as:

- A consultative question opens a project-write permission dialog.
- A pasted example command is treated as the current execution request.
- A bad previous assistant response is reintroduced through context, memory, or session summary.
- The model answers in prose when the task contract requires a tool result.
- Scaffold, local server, memory, and final-answer checks make different assumptions about the same turn.

The fix is not only better prompting. The runtime needs a shared state boundary.

## Refactor Scope

This is a middle refactor, not a full rewrite.

Expected duration if done carefully: 2-4 days.

In scope:

- Introduce `TurnState` as the source of truth for one user turn.
- Route `DesktopAgentService.SendAsync` through `TurnState`.
- Ensure scaffold, local server, task contract, memory, tool loop, and final answer checks use `TurnState` instead of re-reading raw text.
- Keep deterministic safety rules for permission, workspace validation, scaffold plan validation, and tool execution.
- Add focused regression tests for the known bad flows.

Out of scope for this phase:

- Replacing the whole runtime with LangGraph or another framework.
- Rewriting all tool implementations.
- Changing the provider abstraction unless required by `TurnState`.
- Large UI redesign.

## Proposed TurnState

`TurnState` should contain:

```text
RawUserText
RoutingText
UserTurnUnderstanding
EffectiveIntent
ExecutionAllowed
TaskProfile
TaskContract
ProjectScaffoldPlan
LocalServerContract
SelectedSystemSkills
ContextPolicy
MemoryPolicy
ToolPolicy
VerificationPolicy
FinalAnswerPolicy
TraceId
```

Core rule:

```text
After TurnState is created, downstream services must not independently reinterpret raw user text for execution decisions.
```

Raw user text may still be preserved for display, diagnostics, and model reference, but not as an execution authority.

## Phase 1: TurnState Skeleton

Tasks:

- Add a `TurnState` model under `csharp/AgentQ.Desktop/Services`.
- Add a `TurnStateBuilder` or equivalent method.
- Move these values into the state:
  - `UserTurnUnderstanding`
  - `routingText`
  - rule intent
  - LLM/effective intent
  - task profile
  - task contract
  - scaffold plan
  - selected system skills
- Add diagnostics that print the state summary once per turn.

Done when:

- `DesktopAgentService.SendAsync` creates one `TurnState` before routing.
- Existing behavior still builds and focused tests pass.

## Phase 2: Route Through TurnState

Tasks:

- Replace scattered `userText` routing checks with `TurnState` fields.
- Scaffold planning should read `TurnState.RoutingText` and `TurnState.EffectiveIntent`.
- Local server direct path should read `TurnState.TaskContract`.
- Tool loop retry rules should read `TurnState.EffectiveIntent`, `TurnState.TaskProfile`, and `TurnState.TaskContract`.
- Context building should receive `TurnState`, not raw text plus many separate arguments.
- Memory/lesson lookup should use read-only selection first; writes must happen only after meaningful execution evidence.

Done when:

- A `Conversation` turn cannot trigger project scaffold, file write, shell, install, build, delete, commit, or verification.
- A direct `Action` turn still executes through the proper deterministic or tool path.

## Phase 3: Known Failure Regression Tests

Add or keep tests for:

- `새 프로젝트 만들어 보고 싶은데 어떻게 좋을까?` -> Conversation, no scaffold permission.
- `React 주식 분석 사이트 만들어줘` -> Action, scaffold plan path.
- `test2 폴더를 생성해줘` -> Action, creates folder or asks approval according to policy.
- `test2 폴더를 생성해줘 ===== 저는 인공지능이라...` -> MetaFeedback, no folder creation.
- Log/quote containing `삭제해줘`, `생성해줘`, `npm run build` -> no execution unless explicitly re-requested.
- Model returns irrelevant prose for a direct create/delete task -> task-contract fallback or retry.
- Conversation model emits a write tool call -> tool blocked before permission.
- Conversation model emits read-only inspection -> allowed.
- Session summary with bad previous assistant answer -> does not override latest user turn.
- Context building does not increment memory usage counters.

Done when:

- Focused Desktop service tests pass.
- Tool permission tests pass.
- Scaffold tests pass.
- Desktop build passes with zero errors.

## Phase 4: Session Summary And Memory Hygiene

Tasks:

- Session summary should prioritize:
  - file changes
  - verification results
  - tool evidence
  - user-confirmed goals
- Session summary should not preserve a bad assistant answer as if it were the current goal.
- Execution lessons should store compact behavior rules, not raw conversation.
- Context creation should not mutate lesson usage counters.

Done when:

- Resume prompt cannot reintroduce a previous off-target answer as the next task.
- Memory lookup is read-only until execution evidence exists.

## Phase 5: Manual Desktop Smoke Tests

Run in the Desktop UI:

- 상담형:
  - `여기에 새 프로젝트 만들어 보고 싶은데 어떻게 좋을까?`
  - `Agent Q 구조가 이상한데 왜 권한창이 떠?`
- 실행형:
  - `test2 폴더 만들어줘`
  - `notes.md 파일 하나 생성해줘`
- Hybrid:
  - `트리노드 후기 찾아서 정리해줘`
- Meta feedback:
  - bad Agent Q answer pasted with an embedded command
- Scaffold:
  - `React JavaScript 개발자 용어집 웹사이트 만들어줘`
- Local server:
  - `로컬 서버 띄워줘`

Pass criteria:

- UI run steps show the same intent and action decision throughout the turn.
- Permission dialogs appear only for the correct action.
- Final answer matches tool evidence, verification, or a concrete limitation.

## Risks

- `DesktopAgentService.SendAsync` is large and already owns many responsibilities.
- Some tests may currently encode older unsafe behavior.
- Provider outputs may return legacy intent JSON instead of the new understanding JSON.
- Session summary and memory can still contaminate context if not handled after TurnState routing.

## Recommended Order

1. Build `TurnState` without changing behavior.
2. Move routing decisions into `TurnState`.
3. Make downstream services consume `TurnState`.
4. Update tests that expected unsafe legacy behavior.
5. Run focused tests after each step.
6. Only then widen the regression suite.

