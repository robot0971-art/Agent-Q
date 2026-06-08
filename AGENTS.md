# AGENTS.md

## Project Goal / 프로젝트 목표

Agent Q is a C#/.NET desktop coding agent runtime. Its goal is not to be a simple chat wrapper around an LLM.

Agent Q는 단순한 LLM 채팅 앱이 아니라, 실제 개발 작업을 안정적으로 수행하기 위한 **데스크톱 에이전트 런타임**이다.

Core direction:

- Use the model for intent understanding, reasoning, code generation, explanations, and repair suggestions.
- Use deterministic Desktop services for actions that must actually happen: project scaffolding, local server execution, verification, file mutation tracking, and recovery.
- Reduce repeated mistakes through small execution lessons, not by storing long conversations.
- Keep the user in control through explicit plans, approvals, visible run steps, and verification results.

핵심 방향:

- 모델은 의도 해석, 추론, 코드 생성, 설명, 후속 수정에 집중한다.
- 새 프로젝트 생성, 로컬 서버 실행, 검증, 파일 변경 기록처럼 반드시 성공 경로가 필요한 작업은 Desktop Service가 책임진다.
- 긴 대화 저장보다 실행 교훈을 저장해 반복 실수를 줄인다.
- 계획, 승인, 실행 단계, 검증 결과를 UI에 보여 사용자가 흐름을 통제할 수 있게 한다.

## Architectural Principles

### 1. Deterministic execution first

For important actions, do not rely on "the model probably called the right tool."

Prefer primary deterministic paths:

- Safe Scaffold primary path for new project creation.
- Local server primary path for starting/stopping dev servers.
- Verification workflows after scaffold, edits, and repairs.

한국어 요약: 중요한 실행은 모델 도구 호출 운에 맡기지 말고, 승인된 plan이나 명확한 task contract가 있으면 Desktop Service가 직접 수행해야 한다.

### 2. Model loop after the deterministic setup

Use the LLM tool loop for:

- Feature implementation
- Code edits
- Debugging failed verification
- README/docs updates
- Follow-up design and explanation

Do not make the LLM tool loop the main path for greenfield creation or local server startup when a deterministic Desktop service path exists.

### 3. No hidden fallback

Fallback must not mean "secretly create files."

Scaffold execution is allowed only when all are true:

- There is a scaffold plan.
- The user approved it.
- The plan has a valid `planId` and `planHash`.
- The target workspace/path is validated.
- Overwrite rules are respected.

한국어 요약: fallback은 몰래 생성이 아니다. 승인된 plan과 검증된 workspace가 있을 때만 deterministic executor가 실행한다.

### 4. Lessons are behavior rules, not memory dumps

Execution Lesson Memory should store compact rules and failure lessons only.

Do store:

- Intent type
- Failure pattern
- Correct next behavior
- Confidence/success counters

Do not store:

- Full long conversations
- Sensitive data
- Large tool outputs
- User private content unrelated to execution behavior

한국어 요약: Execution Lesson Memory는 긴 대화 저장소가 아니라 반복 실수를 줄이는 행동 규칙 사전이다.

## Current Primary Paths

### Safe Scaffold Mode

Target behavior:

```text
User request
↓
ScaffoldPlanner creates a plan
↓
UI shows files and verification commands
↓
User approves
↓
DesktopScaffoldService / deterministic executor creates files
↓
Verification runs
↓
Agent explains, repairs, or continues implementation
```

Goal sentence:

> Even if the model loop misses a scaffold tool call, an approved scaffold plan should be executed by the Desktop service through a deterministic executor.

한국어 목표 문장:

> 모델 루프가 scaffold 도구 호출을 놓치더라도, 승인된 scaffold plan이 존재하면 Desktop Service가 deterministic executor를 통해 실제 파일 생성을 보장한다.

### Local Server Mode

Target behavior:

```text
User asks to start/stop a local server
↓
UserIntentTranslator creates a TaskContract
↓
DesktopLocalServerService starts, reuses, verifies, or stops the server
↓
UI displays URL/status and exposes Open/Stop actions
↓
Agent provides follow-up explanation or fixes
```

Rules:

- Prefer package scripts in this order: `dev`, `start`, `preview`.
- Use localhost/127.0.0.1 and an available port.
- Verify that the URL responds before reporting success.
- Persist live sessions under `.agentq/local-server/session.json`.
- Reuse a session only when the process is alive and the URL is reachable.

## UX Goals

Agent Q should understand natural user requests without forcing the user to speak tool names.

Examples:

- "여기에 새 프로젝트 만들고 싶다" should become a scaffold planning flow, not an immediate blind file write.
- "React 주식 분석 사이트 만들어줘" should choose a reasonable default stack and generate a plan without unnecessary questioning.
- "로컬서버 띄워줘" should start and verify the local dev server, not merely summarize the project structure.
- "아니 너보고 하라는 게 아니고 Agent Q 반응이 이상하다고" should be treated as meta feedback about Agent Q, not as a direct request for Codex to perform the action.

한국어 요약:

- 사용자가 도구 이름을 몰라도 자연어 의도를 이해해야 한다.
- 애매한 요청은 질문하되, 명확한 greenfield 요청은 너무 자주 막지 않는다.
- 실행 요청과 메타 피드백을 구분해야 한다.

## Guardrails To Preserve

Do not remove or weaken these without a strong reason:

- Workspace path validation
- User approval before risky or file-creating actions
- Plan hash / plan id validation for scaffold execution
- Permission checks for shell/server actions
- Verification after scaffold or meaningful code changes
- File mutation snapshots before edits
- Read-only loop detection
- TaskContract completion checks
- Final-answer consistency checks after file changes

## Verification Expectations

When changing C# desktop services:

- Run focused tests for the changed service.
- Run `dotnet build csharp/AgentQ.Desktop/AgentQ.Desktop.csproj --no-restore` when feasible.
- Prefer narrow test filters first, then broader tests if the change affects shared contracts.

When changing scaffold behavior:

- Test planner output.
- Test deterministic creation path.
- Test verification command selection.
- Test that unsafe/ambiguous requests do not silently create files.

When changing local server behavior:

- Test start, reuse, stop, session persistence, and URL verification.

## Worktree Discipline

The repository may contain unrelated dirty files. Treat them as user work.

- Do not revert unrelated changes.
- Do not stage unrelated changes.
- Use hunk-level staging when files contain mixed changes.
- Keep commits focused and explain what was intentionally included.

한국어 요약:

- 작업트리에 남아 있는 변경은 사용자의 작업일 수 있다.
- 관련 없는 변경을 되돌리거나 커밋에 섞지 않는다.
- 같은 파일 안에 섞인 변경이 있으면 hunk 단위로 선별한다.

## Documentation Notes

`docs/Agent Q.md` summarizes the current architecture. Keep it aligned with these project goals:

- Agent Q is a desktop agent runtime.
- Deterministic Desktop services should own actions that must reliably happen.
- The LLM should assist, explain, repair, and implement after deterministic setup.
- Execution lessons should reduce repeated mistakes without storing long conversations.

