# Agent Q 아키텍처 및 설계 구조 분석

> 분석일: 2026-06-05  
> 대상: Agent Q (C# 기반 AI 코딩 에이전트)

## 검토 결과

`C:\Users\admin\Desktop\Agent Q.md`의 큰 구조 분석은 대체로 맞다. 다만 현재 푸쉬 가능한 코드 기준으로는 몇 가지를 보정해야 한다.

- `EvidenceTrailPanel`이라는 View 이름은 현재 코드 기준으로는 `RunTimelinePanel`이 맞다.
- `AgentCouncilPanel`, `AgentCouncilPanelViewModel`, `DesktopDiagnosticsService`는 현재 워킹트리에는 있으나 별도 미커밋 변경으로 남아 있으므로, 이 문서에서는 푸쉬 대상 구조로 확정하지 않는다.
- 새 프로젝트 생성은 이제 단순한 “모델이 `create_project_scaffold` 도구를 호출하면 생성” 흐름이 아니다. 승인 가능한 scaffold plan이 있으면 Desktop 서비스가 primary path로 직접 실행하는 구조가 핵심이다.
- 로컬 서버 실행도 모델 도구 루프가 아니라 `TaskContract`가 감지한 `RunLocalServer` / `StopLocalServer` 요청을 Desktop 서비스가 직접 처리하는 primary path가 추가되었다.
- `ExecutionLessonMemoryService`는 긴 대화 저장이 아니라 실패/성공에서 뽑은 행동 규칙과 실행 교훈을 `.agentq/lessons`에 저장하는 구조다.

## 1. 프로젝트 전체 구조

```text
csharp/
├── AgentQ.Api                    계약 계층: DTO, 메시지, 도구 정의
├── AgentQ.Core                   공통 핵심: Provider 추상화, 설정, Chat 모델
├── AgentQ.Providers.Anthropic    Anthropic provider 구현
├── AgentQ.Providers.OpenAi       OpenAI/OpenCode Go 호환 provider 구현
├── AgentQ.Tools                  공통 도구: bash, read_file, write_file, edit_file 등
├── AgentQ.Cli                    콘솔 CLI: REPL, 자동화, 도구 루프
├── AgentQ.Desktop                WPF 데스크톱 앱과 에이전트 orchestration 계층
├── AgentQ.MockService            provider parity 테스트용 mock 서버
└── AgentQ.Tests                  단위/통합/데스크톱 서비스 테스트
```

## 2. 계층별 구조

### AgentQ.Api

공유 계약 계층이다. 메시지 요청/응답, content block, stream event, tool definition 같은 DTO를 정의한다.

### AgentQ.Core

provider 추상화와 공통 모델 계층이다. `ILlmProvider`, `IStreamingLlmProvider`, `ProviderFactory`, `ProviderConfiguration`, `ToolCallDeltaBuffer`, `ChatMessage`/`ChatContent` 계열 모델이 이쪽에 있다.

### AgentQ.Providers

실제 LLM provider 구현체다. 현재는 Anthropic 계열과 OpenAI-compatible 계열이 분리되어 있고, OpenAI-compatible provider가 OpenCode Go 호환 흐름도 담당한다.

### AgentQ.Tools

공통 도구 계층이다. `ITool`, `ToolRegistry`, `ToolResult`, `IPermissionEnforcer`가 있고, `BashTool`, `ReadFileTool`, `WriteFileTool`, `EditFileTool`, `GrepTool`, `GlobTool`, `ListDirectoryTool` 같은 기본 도구가 있다.

### AgentQ.Cli

콘솔 실행 계층이다. `CliApplication`, `CliInteractiveConversationRunner`, `CliToolLoopRunner`, `StreamingProcessor`, `ToolExecutor`, `ConversationTurnBuilder`, `ChatConversationHistory`, `SessionStore`, `ConfigStore` 등이 REPL과 non-interactive 실행을 담당한다.

### AgentQ.Desktop

WPF UI와 데스크톱 전용 에이전트 runtime 계층이다.

주요 View/ViewModel:

- `MainWindow`
- `ChatPanel`
- `ProjectPanel`
- `VerificationPanel`
- `PlanPanel`
- `MemoryPanel`
- `GitPanel`
- `RunTimelinePanel`
- `EvalReplayDashboardPanel`
- `MainViewModel`
- `ProjectPanelViewModel`
- `GitPanelViewModel`
- `VerificationPanelViewModel`
- `RunSummaryViewModel`

주요 Service:

- `DesktopAgentService`: 에이전트 핵심 루프, 컨텍스트 조립, provider 호출, 도구 실행, scaffold/local-server primary path 처리
- `DesktopAgentRunWorkflowService`: UI 실행 흐름, 중지, telemetry, permission event 연결, 최종 메시지 처리
- `DesktopPromptAssemblyService`: task profile과 동적 prompt context 조립
- `WorkspaceAnalysisService`: C#, JS/TS, Python, Unity, Unreal, Go, Rust, Docker 등 workspace 분석
- `WorkspaceIndexer` / `WorkspaceSymbolIndexService`: 프로젝트 맵, 키 파일, 심볼 검색 컨텍스트
- `ProjectMemoryService`: `.agentq/memory` 기반 프로젝트 메모리
- `ExecutionLessonMemoryService`: `.agentq/lessons` 기반 실행 교훈 메모리
- `DesktopLocalServerService`: 로컬 dev server 시작/재사용/중지/세션 복구
- `ProjectScaffoldPlanner` / `ProjectScaffoldPlanRegistry`: scaffold plan 생성과 승인 가능한 plan 등록
- `DesktopProjectScaffoldCreateTool` / `DesktopProjectScaffoldVerifyTool`: scaffold 생성 및 검증 도구
- `WorkerScaffoldExecutor`: 승인된 worker scaffold plan의 결정적 파일 생성
- `DesktopVerificationWorkflowService` / `DesktopVerificationRunner`: 검증 명령 실행과 결과 카드 생성
- `DesktopGitService` / `DesktopGitPanelWorkflowService`: git 상태, diff, branch, commit workflow
- `FileMutationSnapshotService`: 변경 전 파일 스냅샷 저장 및 revert 기반
- `ToolReplayService`: 도구 실행 replay 기록
- `DesktopPermissionEnforcer`: WPF 기반 human-in-the-loop 권한 확인

## 3. Desktop 실행 흐름

```text
사용자 입력
↓
DesktopAgentRunWorkflowService.SendCurrentMessageAsync()
↓
MainViewModel에 User/Assistant placeholder 추가
↓
DesktopPermissionEnforcer 생성
↓
DesktopAgentService.SendAsync()
↓
Workspace/Memory/Skill/TaskContract/Scaffold context 조립
↓
TaskContract primary path 확인
├─ RunLocalServer/StopLocalServer → DesktopLocalServerService 직접 실행
├─ 승인된 Safe Scaffold plan → deterministic scaffold executor 직접 실행
└─ 그 외 요청 → provider stream + tool loop
↓
도구 실행, 파일 변경 기록, 검증 계획/결과 수집
↓
confidence/replay/lesson 기록
↓
UI 최종 메시지와 run summary 갱신
```

## 4. 새 프로젝트 생성 구조

현재 설계의 핵심은 Safe Scaffold를 fallback이 아니라 primary path로 승격한 점이다.

```text
사용자 요청
↓
ProjectScaffoldPlanner가 greenfield 의도와 scaffold plan 생성
↓
planId / planHash / workspace 검증
↓
사용자 승인
↓
Desktop 서비스가 deterministic executor로 파일 생성
↓
검증 명령 실행
↓
LLM은 설명, 후속 수정, 실패 복구를 담당
```

중요한 점:

- 파일 생성 책임은 모델이 아니라 Desktop 서비스에 있다.
- plan, approval, workspace 검증이 모두 있을 때만 실행한다.
- 모델이 `create_project_scaffold` 호출을 놓쳐도 승인된 plan이 있으면 Desktop service가 생성 경로를 보장한다.

## 5. 로컬 서버 실행 구조

로컬 서버 요청도 `TaskContract` 기반 primary path다.

```text
"로컬 서버 띄워줘" / "서버 꺼줘"
↓
UserIntentTranslator
↓
TaskContractIntent.RunLocalServer 또는 StopLocalServer
↓
DesktopLocalServerService
├─ package.json scripts에서 dev/start/preview 선택
├─ 사용 가능한 localhost port 선택
├─ npm run <script> -- --host 127.0.0.1 --port <port>
├─ URL 응답 검증
├─ .agentq/local-server/session.json 저장
└─ 기존 세션 생존 + URL 응답 시 재사용
```

UI는 `DesktopLocalServerState` 콜백으로 서버 상태를 받아 하단 바에 표시한다. `Open`은 URL을 브라우저로 열고, `Stop`은 기존 AgentQ 실행 흐름을 통해 deterministic stop path로 들어간다.

## 6. Execution Lesson Memory

`ExecutionLessonMemoryService`는 긴 대화 로그를 저장하지 않는다. 목적은 “행동 규칙”과 “실패 회피 교훈”만 저장하는 것이다.

저장 위치:

```text
.agentq/lessons/execution-lessons.json
.agentq/lessons/execution-lesson-events.jsonl
```

기록 대상:

- task contract intent
- 실패 요약
- 다음 실행에서 지켜야 할 교훈
- 적용 횟수
- 성공/실패 기반 confidence 조정

이 구조는 “AgentQ가 긴 대화를 외운다”가 아니라 “반복 실수를 줄이는 작은 실행 사전”에 가깝다.

## 7. Guardrail 구조

AgentQ는 LLM 응답만 믿지 않고 코드 레벨 guardrail을 둔다.

- Empty response guard
- No-tool coding guard
- Generic greeting guard
- Manual fallback guard
- Read-only loop guard
- TaskContract completion checker
- Irrelevant final response guard
- Safe Scaffold primary executor
- Local server primary executor

이 때문에 사용자가 “로컬 서버 띄워줘”라고 했을 때 단순 구조 설명으로 끝나는 반응을 줄이고, 실제 실행이 필요한 요청은 Desktop 서비스가 직접 처리할 수 있다.

## 8. 상태 관리

AgentQ는 하나의 거대한 상태 기계라기보다 UI 상태, 실행 이벤트, 파일 변경 기록, 검증 기록이 결합된 event-driven 구조다.

- 대화 상태: `DesktopAgentService._messages`, CLI `ChatConversationHistory`
- 실행 상태: `AgentRunState`
- UI 상태: `MainViewModel` + `ObservableCollection<T>` + `INotifyPropertyChanged`
- 파일 변경 상태: `FileChangeRecord`
- 검증 상태: `VerificationResultCard`
- 계획 상태: `AgentPlanItem`, `WorkerExecutionContext`
- 로컬 서버 상태: `DesktopLocalServerState`, `LocalServerSession`

## 9. 설계 철학

Agent Q의 방향은 단순한 ChatGPT wrapper가 아니라, 실제 개발 workflow를 보조하는 데스크톱 에이전트 runtime에 가깝다.

핵심 철학은 세 가지다.

1. 결정적 실행 경로: 새 프로젝트 생성, 로컬 서버 실행처럼 성공 경로가 명확해야 하는 작업은 Desktop service가 책임진다.
2. 관찰 가능성: run step, replay, verification, confidence, lesson memory로 행동 근거를 남긴다.
3. 반복 실수 감소: 긴 대화 저장보다 실행 교훈을 저장해 같은 실패 패턴을 줄인다.

