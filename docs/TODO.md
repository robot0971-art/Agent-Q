# Agent Q 설계 감사 TODO

> 인코딩: UTF-8  
> 목적: Agent Q가 사용자의 실제 요청과 다른 엉뚱한 답변을 하거나, 실행하지 않은 일을 완료했다고 말하는 설계 오류를 끝까지 찾아 고친다.

## 현재 목표

- [ ] Agent Q 전체 코드를 파일 단위로 계속 감사한다.
- [ ] 사용자의 최신 요청이 과거 대화, 붙여넣은 예시, 세션 요약, 체크포인트, 메모리, scaffold 힌트, verification 힌트에 덮이지 않게 한다.
- [ ] 대화형 요청과 실행형 요청을 끝까지 분리한다.
- [ ] 파일 생성, 수정, 삭제, 서버 실행, 빌드, 테스트, 설치, 커밋 같은 실제 실행은 deterministic Desktop 서비스와 명확한 승인/검증 경로를 거치게 한다.
- [ ] 모델이 도구를 쓰지 않고 “완료했다”고 말하는 경우를 신뢰하지 않는다.
- [ ] 모든 수정은 회귀 테스트와 빌드로 확인한다.

## 현재 상태 요약

- 전체 프로젝트 기준으로는 아직 절반 미만이다.
- 핵심 원인 경로인 `DesktopAgentService`, intent 분류, task contract, scaffold, provider tool-call, session summary, checkpoint, prompt context, file-change evidence 쪽은 꽤 많이 감사했다.
- 아직 worker/plan execution 전체, verification repair loop 전체, CLI 전체, Tools 전체, API/Core 일부, UI 이벤트 전체, docs 정리는 더 남아 있다.
- 다른 컴퓨터에서 이어가기 전에는 이 파일과 현재 WIP 브랜치/커밋을 기준으로 이어가면 된다.

## 완료한 작업

### 1. 원래 사고 재현 경로 방어

- [x] `test2 폴더를 생성해줘` 같은 실제 실행 요청이 붙여넣은 오답 예시와 섞였을 때, 예시를 현재 실행 요청으로 오해하지 않게 했다.
- [x] `=====` 뒤에 붙은 이전 Agent Q 오답을 embedded evidence로 분리한다.
- [x] 메타 피드백성 문장, 예를 들어 “Agent Q가 자꾸 엉뚱한 답을 한다”는 요청은 파일 생성 요청이 아니라 분석/수정 요청으로 분류한다.
- [x] quoted text, 로그, 예시 안에 들어 있는 실행형 문장을 현재 명령으로 실행하지 않게 했다.
- [x] fenced code block 안의 `test2 폴더를 생성해줘` 같은 문장도 현재 요청이 아니라 로그/예시 evidence로 분리한다.

### 2. Intent / Task Contract / 최신 요청 우선권

- [x] `Conversation`, `Action`, `Hybrid`, `Ambiguous` 분류를 보강했다.
- [x] 모델이 `Conversation`을 write/shell `Action`으로 승격하려 할 때 안전 규칙으로 차단한다.
- [x] deterministic fallback이 concrete action을 찾았는데 모델이 다른 action kind로 바꾸면 fallback action을 유지한다.
- [x] “방법 알려줘”, “어떻게 해?”, “가능한가?” 같은 consultative/how-to 요청은 실행 계약을 만들지 않게 했다.
- [x] `로컬서버 실행 방법 알려줘`는 서버를 실행하지 않는다.
- [x] `폴더 생성 방법 알려줘`는 폴더를 만들지 않는다.
- [x] `yarn dev` 같이 공백 제거 후 깨지던 로컬 서버 실행 패턴을 보강했다.
- [x] 최신 사용자 요청을 transient context 맨 앞에 명시하고, 과거 context와 충돌하면 최신 요청을 따르도록 했다.

### 3. No-tool 완료 환각 방어

- [x] 모델이 도구 호출 없이 “폴더가 생성되었습니다”, “완성되었습니다”라고 말해도 파일 변경/도구 replay evidence가 없으면 완료로 인정하지 않게 했다.
- [x] no-tool mutation summary는 tool evidence가 있을 때만 허용한다.
- [x] no-tool completion retry 이후에도 도구 호출 없이 반복되면 guard message로 중단한다.
- [x] task-contract completion checker가 concrete action에 필요한 도구 evidence를 요구하도록 했다.
- [x] permission denied, tool failure 같은 한계 보고도 실제 failed replay evidence가 있을 때만 task contract를 만족하게 했다.

### 4. Provider tool-call 파싱

- [x] OpenAI-compatible legacy `function_call`을 tool-use content로 변환한다.
- [x] OpenAI-compatible streaming legacy `function_call` delta를 처리한다.
- [x] OpenAI-compatible streaming parser가 여러 `data:` line으로 쪼개진 SSE tool-call JSON을 버리지 않게 했다.
- [x] Anthropic streaming parser가 SSE comment/heartbeat와 multi-line `data:` event를 버리지 않게 했다.
- [x] whitespace-only tool name/id를 유효한 tool call로 취급하지 않게 했다.
- [x] whitespace-only provider tool id는 replay id로 그대로 보존하지 않고 생성 id로 대체한다.
- [x] OpenAI/Anthropic max token 설정이 0 또는 너무 큰 값일 때 provider DTO 범위를 깨지 않게 했다.

### 5. 세션 요약 / 체크포인트 / live 대화 히스토리 오염 방지

- [x] session summary resume prompt는 저장된 요약을 “historical evidence only”로 명시한다.
- [x] checkpoint resume prompt는 저장된 대화/로그를 “fresh user request”로 취급하지 않게 명시한다.
- [x] 파일 변경이 있었을 때 session summary가 엉뚱한 assistant 답변 대신 파일 변경/검증 중심 narrative를 저장하게 했다.
- [x] 파일 변경이 없더라도 명백한 독서/게임 off-target assistant 답변은 session summary에서 생략한다.
- [x] checkpoint 저장도 파일 변경 유무와 관계없이 명백한 off-target assistant 답변을 생략한다.
- [x] provider request assembly 단계에서 live conversation history에 남아 있는 off-target assistant 답변을 원문 그대로 보내지 않고 생략 메모로 대체한다.

### 6. Context assembly 실행 오염 방지

- [x] actionable task contract가 없으면 feature execution strategy와 scaffold decision hint를 transient context에 붙이지 않는다.
- [x] “언리얼 플레이어 컨트롤러 가능한가?” 같은 feasibility 질문에 scaffold/file creation 힌트가 붙지 않게 했다.
- [x] scaffold recommendation preview는 consultative project/website 질문에 approval/execution context를 붙이지 않는다.
- [x] project memory는 현재 query에 맞는 lesson만 선택하도록 했다.
- [x] queryless memory context가 off-target 독서/게임 조언을 주입하지 않도록 필터링했다.
- [x] hybrid search도 project memory lesson을 usefulness/relevance gate로 필터링한다.

### 7. Scaffold / Worker / Project 생성 경로

- [x] 단일 폴더 생성 요청은 project scaffold가 아니라 create_directory 경로로 간다.
- [x] bare new-project 요청은 깨지지 않은 한국어 clarification을 보여준다.
- [x] 명시적 파일/코드 수정 요청은 `App` 같은 단어 때문에 greenfield scaffold로 오인하지 않는다.
- [x] unsupported Unreal/Unity/Godot 등 engine-specific 요청은 generic Vite/React scaffold로 만들지 않고 질문하거나 지원 경로로 넘긴다.
- [x] worker plan에서 command step은 승인 없이 Ready가 되지 않는다.
- [x] path 없는 human plan item은 `RunCommand`가 아니라 manual step으로 남긴다.
- [x] worker scaffold 실행 버튼/서비스는 create-file step이 없는 plan을 scaffold 실행하지 않는다.
- [x] worker scaffold-created file과 auto-wiring edit도 일반 tool mutation처럼 snapshot을 남긴다.
- [x] worker repair plan은 allowed verification command를 보존하고 stale repair state를 정리한다.
- [x] project scaffold create/verify tool은 invalid path를 throw하지 않고 structured error로 반환한다.
- [x] project scaffold verification repair plan의 command도 verification policy로 필터링한다.
- [x] `create_project_scaffold`는 이전 run-wide `ProjectWrite` 승인 재사용 없이 plan-specific approval을 요구한다.

### 8. 파일 변경 / workspace boundary / symlink 방어

- [x] `WorkspacePathResolver`로 resolved workspace-boundary check를 중앙화했다.
- [x] worker/project scaffold path는 symlink/reparse directory escape를 거부한다.
- [x] worker scaffold auto-wiring target도 symlink/reparse escape를 거부한다.
- [x] file-change review revert는 resolved boundary check 후에만 이전 내용을 복원하거나 생성 파일을 삭제한다.
- [x] workspace indexer, embedding indexer, source browser, hybrid search, symbol index, dependency graph, workspace analysis가 reparse/symlink outside file을 자동 context로 읽지 않게 했다.
- [x] grep/glob recursive search는 reparse directory를 명시적으로 건너뛰며 outside symlink를 따라가지 않는다.
- [x] permission classifier는 symlink directory 아래 existing file도 `ExternalWrite`로 분류한다.
- [x] file-change snapshot recording도 resolved workspace-boundary check를 사용한다.

### 9. Verification / Run outcome / Confidence

- [x] shell verification command는 JSON result의 `exitCode`가 0일 때만 `executedCommands`에 기록한다.
- [x] failed `dotnet test`/`dotnet build`를 completed verification evidence로 쓰지 않는다.
- [x] verification panel command execution은 install/network command가 일반 tool permission policy를 우회하지 못하게 했다.
- [x] cancelled verification은 fixable failure나 last failure state를 덮지 않는다.
- [x] suggested/manual verification plan은 active verifying event가 아니라 planning/done 상태로 기록한다.
- [x] standalone static HTML/CSS/JS 변경에는 package manifest가 없으면 `npm run build`를 요구하지 않는다.
- [x] simple directory creation은 read/search/build가 없다는 이유만으로 Low confidence가 되지 않게 했다.
- [x] file-change replacement summary는 “완료”를 과장하지 않고 기록된 변경/검증 evidence 중심으로 말한다.
- [x] max tool step stop 후에도 file-change evidence를 보존한다.
- [x] top-level run completion은 step-limit, no-tool guard, task-contract rejection을 successful `Response complete`로 보지 않는다.
- [x] scaffold failure, scaffold-not-created, local-server failure도 successful completion으로 보지 않는다.
- [x] run summary/status accent는 guard stop, tool-step limit, scaffold-not-created를 completed/green으로 보이지 않게 했다.
- [x] automatic session-summary save가 실패/guard 상태를 `Session summary auto-saved`로 덮지 않게 했다.

### 10. Local server / CLI / Tool parity

- [x] local server stale PID/session reuse를 start time까지 비교해 거부한다.
- [x] local server direct execution은 replay evidence를 남긴다.
- [x] local server failure는 permission denial과 process start failure를 구분한다.
- [x] CLI non-interactive runner는 max-step stop을 completed로 보고하지 않는다.
- [x] CLI non-interactive runner는 bash JSON `exitCode != 0`을 tool failure로 본다.
- [x] interactive CLI session permission reuse는 `web_search`에만 허용하고 bash/file mutation/delete/create에는 재사용하지 않는다.
- [x] interactive CLI `/run`은 JSON parse/normalize 후 permission prompt를 띄운다.
- [x] read-only shell inspection도 Coding mode에서 approval을 요구하고 Readonly mode에서는 blocked된다.
- [x] Conversation intent turn은 read-only bash shell inspection도 permission prompt 전에 차단한다.

### 11. Git / Commit / Docs / Build reliability

- [x] Git panel commit은 staged file이 모두 Approved 상태일 때만 `git commit`을 실행한다.
- [x] stale ignored `AgentQ.Desktop_*_wpftmp.csproj`로 WPF `.g.cs` missing build가 반복되던 문제를 확인하고 clean rebuild 절차를 기록했다.
- [x] `dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false`가 통과하는 상태를 여러 차례 확인했다.

## 앞으로 해야 할 일

### A. 전체 감사 진행 방식

- [ ] 새 컴퓨터에서 먼저 현재 WIP 브랜치 또는 압축본을 받아온다.
- [ ] `git status --short`로 변경 파일 목록을 확인한다.
- [ ] `docs/TODO.md`가 UTF-8로 깨지지 않고 열리는지 확인한다.
- [ ] `dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false`를 먼저 실행한다.
- [ ] 빌드가 WPF 임시 `.g.cs` missing 오류를 내면 `csharp\AgentQ.Desktop\obj\Debug\net10.0-windows`를 안전하게 삭제한 뒤 다시 빌드한다.
- [ ] 감사를 계속할 때는 한 번에 한 영역만 읽고, 발견한 설계 오류마다 테스트와 빌드를 붙인다.
- [ ] 관련 없는 dirty file은 되돌리지 않는다.
- [ ] 같은 파일 안에 사용자 작업과 감사 수정이 섞이면 hunk 단위로 분리한다.

### B. 가장 먼저 이어서 볼 영역

- [ ] `csharp/AgentQ.Desktop/Services/WorkerExecutionPipeline.cs`
  - [ ] worker plan 실행 상태가 실패인데 성공처럼 반환되는 경로가 없는지 확인한다.
  - [ ] partial success가 전체 success로 둔갑하지 않는지 확인한다.
  - [ ] repair plan 생성/삭제 조건이 stale 상태를 남기지 않는지 확인한다.
  - [ ] verification 실패가 worker plan 성공으로 덮이지 않는지 확인한다.

- [ ] `csharp/AgentQ.Desktop/Services/WorkerScaffoldExecutor.cs`
  - [ ] create-file/write 실패가 structured issue로 올라오는지 끝까지 확인한다.
  - [ ] directory exists, file exists, parent missing, permission denied 케이스를 각각 확인한다.
  - [ ] symlink/reparse parent 경로가 모든 write 전 단계에서 차단되는지 확인한다.
  - [ ] generated file list와 실제 file mutation snapshot이 일치하는지 확인한다.

- [ ] `csharp/AgentQ.Desktop/Services/WorkerScaffoldAutoWirer.cs`
  - [ ] React/FastAPI/Rust auto-wiring target 선택이 최신 사용자 요청을 벗어나지 않는지 확인한다.
  - [ ] 기존 파일을 수정하기 전에 snapshot이 반드시 남는지 확인한다.
  - [ ] auto-wiring 실패가 scaffold 전체 성공처럼 보이지 않는지 확인한다.

- [ ] `csharp/AgentQ.Desktop/Services/DesktopPlanCommandService.cs`
  - [ ] plan approval 없이 실행으로 넘어가는 버튼/메서드가 없는지 확인한다.
  - [ ] manual-only plan, command-only plan, modify-only plan이 scaffold 실행으로 넘어가지 않는지 확인한다.
  - [ ] worker repair prompt가 사용자 draft를 덮지 않는지 확인한다.
  - [ ] checkpoint/resume/generated prompt가 현재 사용자 입력을 덮지 않는지 재확인한다.

- [ ] `csharp/AgentQ.Desktop/Services/DesktopVerificationCommandService.cs`
  - [ ] verification fix prompt가 실패 원문과 최신 사용자 요청을 뒤섞지 않는지 확인한다.
  - [ ] auto-fix와 manual fix가 서로 다른 상태를 덮지 않는지 확인한다.
  - [ ] cancelled verification과 failed verification이 UI/메모리/summary에 다르게 남는지 확인한다.

- [ ] `csharp/AgentQ.Desktop/Services/DesktopVerificationPanelWorkflowService.cs`
  - [ ] 마지막 failure state가 성공/취소/새 요청에 의해 잘못 덮이지 않는지 확인한다.
  - [ ] verify command policy가 모든 진입점에서 동일하게 적용되는지 확인한다.
  - [ ] output preview가 너무 길거나 민감한 내용을 저장하지 않는지 확인한다.

### C. DesktopAgentService 남은 세부 감사

- [ ] `ExecuteToolsAsync` 전체를 다시 읽는다.
  - [ ] tool result가 error인데 replay evidence가 success처럼 쓰이는 경로가 없는지 확인한다.
  - [ ] tool call id가 중복/비어 있음/공백일 때 message 흐름이 깨지지 않는지 확인한다.
  - [ ] tool failure 후 retry instruction이 최신 task contract를 유지하는지 확인한다.
  - [ ] tool loop 중간에 transient context가 반복 삽입되지 않는지 확인한다.

- [ ] final-answer consistency guard를 다시 읽는다.
  - [ ] file change가 있는데 final answer가 무관할 때 항상 replacement summary가 적용되는지 확인한다.
  - [ ] verification failure가 있는데 final answer가 성공처럼 말할 때 guard가 막는지 확인한다.
  - [ ] local server failure가 있는데 final answer가 URL 성공처럼 말할 때 막는지 확인한다.
  - [ ] scaffold failure가 있는데 final answer가 created files를 말할 때 막는지 확인한다.

- [ ] direct deterministic fallback 경로를 다시 읽는다.
  - [ ] create directory direct path가 explicit target만 생성하는지 확인한다.
  - [ ] delete path direct path가 explicit target만 삭제하는지 확인한다.
  - [ ] local server direct path가 session/replay/verification evidence를 모두 남기는지 확인한다.
  - [ ] 직접 실행 경로와 LLM tool-loop 경로의 confidence/reporting이 일관적인지 확인한다.

- [ ] `BuildContextOnlyAsync`를 한 번 더 읽는다.
  - [ ] memory, workspace snapshot, scaffold plan, execution lesson이 최신 요청보다 위에 서지 않는지 확인한다.
  - [ ] conversation-only turn에 task-contract context가 붙지 않는지 확인한다.
  - [ ] link auto-read failure가 “웹 접근 불가” 환각으로 이어지지 않는지 확인한다.

### D. Intent / Classifier / Korean phrasing 남은 감사

- [ ] `TurnIntentClassifier.cs`
  - [ ] 한국어 생성/수정/삭제/실행/검증 동사가 빠진 것이 없는지 확인한다.
  - [ ] “하고 싶다”, “가능할까”, “방법” 같은 consultative 표현이 Action으로 승격되지 않는지 확인한다.
  - [ ] “해줘”, “만들어줘”, “돌려줘” 같은 명령형은 concrete target이 있을 때 Action이 되는지 확인한다.
  - [ ] action-like example과 current command를 구분하는 테스트를 더 추가한다.

- [ ] `UserTurnUnderstanding.cs`
  - [ ] `=====` 외의 구분자, 예를 들어 `-----`, `---`, `````, quote block이 잘 분리되는지 확인한다.
  - [ ] 이미지 설명 질문, “이건 무슨 뜻이지?” 같은 요청이 embedded screenshot/text를 실행하지 않는지 확인한다.
  - [ ] 모델 classifier JSON이 fallback보다 위험하게 승격될 때 항상 차단되는지 확인한다.
  - [ ] 모델 classifier가 action target을 path-only로 줄여도 routing text는 원래 action verb를 보존하는지 확인한다.

- [ ] `UserIntentTranslator.cs` / `TaskContract.cs`
  - [ ] create file, create directory, delete path, run verification, run local server가 서로 섞이지 않는지 확인한다.
  - [ ] shell command를 설명해달라는 요청과 실행해달라는 요청을 더 많이 테스트한다.
  - [ ] “이 폴더”, “여기”, “현재 폴더” 같은 deictic word가 target 이름으로 잘못 쓰이지 않는지 추가 확인한다.

- [ ] `DesktopTaskClassifier.cs`
  - [ ] product review와 code review를 더 분리한다.
  - [ ] feature feasibility question과 feature implementation request를 더 분리한다.
  - [ ] documentation/update 요청이 실제 file write인지 설명 요청인지 확인한다.

### E. Project Memory / Execution Lesson 남은 감사

- [ ] `ProjectMemoryService.cs`
  - [ ] local/shared memory file merge 순서가 최신/로컬 정보를 과도하게 덮지 않는지 확인한다.
  - [ ] preference/check/context fact가 query 없이 과도하게 prompt에 들어가지 않는지 확인한다.
  - [ ] sensitive value 필터가 API key, bearer token, password, env var를 충분히 걸러내는지 확인한다.
  - [ ] off-target 조언 필터가 너무 좁아서 다른 형태의 무관한 조언을 저장하지 않는지 확인한다.
  - [ ] 반대로 실제 게임 개발/독서 앱 개발 문맥까지 잘못 제거하지 않는지 확인한다.

- [ ] `ExecutionLessonMemoryService.cs`
  - [ ] 실패 lesson이 성공 경로에서 잘못 강화되지 않는지 확인한다.
  - [ ] confidence 감소/비활성화 규칙이 너무 늦거나 빠르지 않은지 확인한다.
  - [ ] action intent별 lesson 선택이 최신 요청과 맞는지 확인한다.
  - [ ] execution lesson이 conversation turn에 실행 압력을 주지 않는지 확인한다.

- [ ] `DesktopLearningSuggestionService.cs`
  - [ ] 실패 lesson 후보가 너무 쉽게 만들어지지 않는지 확인한다.
  - [ ] provider/model failure lesson이 민감한 error detail을 저장하지 않는지 확인한다.
  - [ ] user가 직접 저장하기 전 pending lesson이 prompt context에 들어가지 않는지 확인한다.

### F. Provider / Core 남은 감사

- [ ] `csharp/AgentQ.Core/Models/ChatModels.cs`
  - [ ] role/content model이 provider별 변환에서 손실되지 않는지 확인한다.
  - [ ] tool result가 user role로 들어가는 구조가 모든 provider와 호환되는지 확인한다.
  - [ ] compacted message가 tool-call protocol을 깨지 않는지 확인한다.

- [ ] `ToolCallDeltaBuffer.cs`
  - [ ] multiple tool calls, partial arguments, duplicate index, missing id/name 케이스를 더 확인한다.
  - [ ] malformed JSON argument가 provider layer에서 tool execution까지 가지 않는지 확인한다.

- [ ] `OpenAiCompatibleProvider.cs`
  - [ ] Chat Completions와 OpenAI-compatible provider가 tool call schema를 다르게 주는 케이스를 확인한다.
  - [ ] streaming/non-streaming response에서 reasoning/text/tool-use가 섞일 때 누락이 없는지 확인한다.
  - [ ] tool-call argument가 object/string/empty/malformed일 때 behavior를 통일한다.

- [ ] `AnthropicProvider.cs`
  - [ ] content block start/delta/stop 순서 변형을 더 확인한다.
  - [ ] tool result id가 비거나 공백일 때 안전하게 drop되는지 확인한다.
  - [ ] usage 누락/부분 usage가 run workflow에 문제를 일으키지 않는지 확인한다.

- [ ] provider configuration
  - [ ] timeout, max token, base URL, model, vision-review 설정이 UI/CLI/env/file 사이에서 일관적인지 확인한다.
  - [ ] 보호 저장/로드가 설정을 누락하지 않는지 확인한다.

### G. Tools 남은 감사

- [ ] `BashTool.cs`
  - [ ] exit code, stdout/stderr, timeout, working directory, JSON result 형식이 모든 caller에서 일관되게 해석되는지 확인한다.
  - [ ] destructive command guard가 PowerShell/CMD/Git 변형을 충분히 막는지 확인한다.
  - [ ] command output 안의 prompt injection을 Agent Q가 지시로 따르지 않게 context wording을 확인한다.

- [ ] `ReadFileTool.cs`
  - [ ] symlink/reparse outside file을 읽지 않는지 확인한다.
  - [ ] 큰 파일, binary file, encoding 문제를 안전하게 처리하는지 확인한다.
  - [ ] line range가 잘못된 경우 structured error를 반환하는지 확인한다.

- [ ] `WriteFileTool.cs` / `EditFileTool.cs`
  - [ ] path boundary와 approval policy가 Desktop과 CLI에서 일관적인지 확인한다.
  - [ ] overwrite, create new, empty file, encoding, newline 처리 방식을 확인한다.
  - [ ] edit 실패가 partial write를 남기지 않는지 확인한다.

- [ ] `ListDirectoryTool.cs`
  - [ ] hidden file, reparse directory, large directory paging을 확인한다.
  - [ ] directory listing 결과가 너무 커서 prompt를 오염시키지 않는지 확인한다.

- [ ] `GrepTool.cs` / `GlobTool.cs`
  - [ ] explicit no-follow traversal이 모든 OS에서 동작하는지 확인한다.
  - [ ] huge result cap, binary skip, ignored directory 처리 방식을 확인한다.

- [ ] `WebSearchTool.cs`
  - [ ] max_results clamp, query validation, network failure reporting을 확인한다.
  - [ ] search result가 최신 사용자 요청과 무관한 context로 과하게 들어가지 않는지 확인한다.

### H. CLI 남은 감사

- [ ] `CliToolLoopRunner.cs`
  - [ ] Desktop과 같은 no-tool guard/task-contract failure 규칙을 갖는지 확인한다.
  - [ ] max step stop이 automation success로 보고되지 않는지 추가 확인한다.

- [ ] `CliNonInteractiveRunner.cs`
  - [ ] automation output schema가 success/failure를 명확히 구분하는지 확인한다.
  - [ ] bash non-zero exit code, tool error, guard stop이 모두 failure로 전달되는지 확인한다.

- [ ] `CliInteractiveToolCommands.cs`
  - [ ] `/run` direct tool call이 Desktop permission policy와 너무 다르지 않은지 확인한다.
  - [ ] malformed JSON이 permission prompt를 열지 않는지 추가 확인한다.

- [ ] `ConsolePermissionEnforcer.cs`
  - [ ] approval reuse가 의도한 tool에만 적용되는지 확인한다.
  - [ ] external write, destructive, network, shell, project write risk level 문구가 명확한지 확인한다.

### I. UI 이벤트 흐름 남은 감사

- [ ] `MainWindow.xaml.cs`
  - [ ] 모든 버튼 이벤트가 사용자 draft를 덮지 않는지 확인한다.
  - [ ] Continue, Resume checkpoint, Resume summary, Fix verification, Auto fix가 서로 상태를 잘못 공유하지 않는지 확인한다.
  - [ ] busy 상태에서 새 요청이 들어올 때 stop/redirect 흐름이 안전한지 확인한다.

- [ ] `MainViewModel.cs`
  - [ ] status color/accent가 실패를 성공처럼 보이지 않게 하는지 확인한다.
  - [ ] `CanExecuteWorkerScaffold`, `CanContinueLastRun`, `CanResume...` 계산이 stale 상태를 남기지 않는지 확인한다.
  - [ ] file change review status가 Git commit 승인 상태와 일관적인지 확인한다.

- [ ] panel view models
  - [ ] Agent council, run timeline, verification panel, git panel, memory panel이 실행 상태를 과장하지 않는지 확인한다.
  - [ ] confidence Low/Medium/High 문구가 실제 evidence와 맞는지 확인한다.

### J. 문서 / 인코딩 / 정리

- [ ] `docs/Agent Q.md`를 현재 architecture와 맞춘다.
  - [ ] deterministic Desktop service 우선 원칙을 반영한다.
  - [ ] LLM은 reasoning/repair/explanation에 집중한다는 원칙을 반영한다.
  - [ ] lesson memory는 긴 대화 저장소가 아니라 행동 규칙 저장소라는 점을 반영한다.

- [ ] 깨진 한글(mojibake)이 남아 있는 테스트 이름, 주석, 문서 항목을 정리한다.
  - [ ] 사용자-facing 문구는 UTF-8 한글로 유지한다.
  - [ ] source code string literal에서 encoding 위험이 있으면 `\u` escape를 사용한다.
  - [ ] 의미가 불확실한 깨진 문장은 임의 복원하지 말고 새 테스트 문장으로 대체한다.

- [ ] 삭제된 docs 파일이 의도된 삭제인지 확인한다.
  - [ ] `docs/hermes-agent-porting.ko.md`
  - [ ] `docs/hermes-import-notes.md`
  - [ ] `docs/superpowers/plans/2025-04-07-pork-damage-flash.md`

- [ ] 새로 생긴 문서 파일이 필요한지 확인한다.
  - [ ] `docs/DEVELOPMENT_PLAN.md`
  - [ ] `docs/TODO.md`

### K. 최종 완료 조건

- [ ] `csharp/AgentQ.Desktop/Services` 전체 파일을 직접 읽었다.
- [ ] `csharp/AgentQ.Desktop` UI/event 흐름을 직접 읽었다.
- [ ] `csharp/AgentQ.Core` 전체 provider abstraction/message/tool-call core를 직접 읽었다.
- [ ] `csharp/AgentQ.Providers.OpenAi` 전체를 직접 읽었다.
- [ ] `csharp/AgentQ.Providers.Anthropic` 전체를 직접 읽었다.
- [ ] `csharp/AgentQ.Tools` 전체 tool 구현을 직접 읽었다.
- [ ] `csharp/AgentQ.Cli` 전체 CLI 실행/permission/tool loop를 직접 읽었다.
- [ ] `csharp/AgentQ.Tests`에서 핵심 guardrail coverage가 실제 요구사항을 커버하는지 확인했다.
- [ ] 원래 사고, 즉 “구체적인 생성 요청 + 붙여넣은 엉뚱한 답변 예시”가 end-to-end로 안전하게 처리되는지 검증했다.
- [ ] 주요 회귀 테스트 묶음을 통과시켰다.
- [ ] `dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false`가 통과했다.
- [ ] 남은 위험과 의도적으로 미룬 UX 개선을 문서에 분리해 기록했다.

## 최근 실행한 검증 명령

```powershell
dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopSessionSummaryBuilder_|FullyQualifiedName~DesktopCheckpointWorkflowService_|FullyQualifiedName~DesktopAgentService_ContextForUnrealFeasibilityQuestionAvoidsSessionAndLinkDrift|FullyQualifiedName~DesktopAgentService_BuildRequestMessagesOmitsOffTargetHistoricalAssistantText|FullyQualifiedName~UserTurnUnderstanding_|FullyQualifiedName~DesktopWorkspaceContextWorkflowService_AutoSessionSummaryDoesNotOverwriteRunStatus" /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_RecordsCreateDirectoryAsFileChange|FullyQualifiedName~DesktopAgentService_RecordsEmptyFileDeletionAsFileChange|FullyQualifiedName~DirectorySymlink|FullyQualifiedName~WorkspacePathResolver|FullyQualifiedName~ToolPermissionClassifier_|FullyQualifiedName~FileMutationSnapshotService" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false
```

## 다른 컴퓨터에서 이어가기

1. WIP 브랜치를 사용하는 경우:

```powershell
git fetch
git switch codex/agentq-audit-wip
git status --short
dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false
```

2. 압축 파일로 옮기는 경우:

- `.git` 폴더를 포함한다.
- `csharp` 폴더를 포함한다.
- `docs` 폴더를 포함한다.
- `bin`, `obj`는 제외해도 된다.
- 로컬 API 키나 `.env`류는 별도로 확인한다.

## 운영 메모

- UX 개선은 나중에 한다.
- 지금 우선순위는 설계 오류, 실행 경계, 최신 요청 보존, 검증/상태 진실성이다.
- 관련 없는 dirty file은 되돌리지 않는다.
- 커밋할 때는 가능하면 WIP 브랜치에 먼저 저장한다.
- main에 바로 커밋하지 말고, 안정화 후 merge한다.
