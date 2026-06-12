# Agent Q 설계 감사 TODO

> 인코딩: UTF-8  
> 목적: Agent Q의 모든 설계 오류를 끝까지 찾아 고친다.

## 현재 목표

- [ ] Agent Q 전체 코드를 파일 단위로 글자 하나 놓치지 않고  계속 감사한다.
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

- [x] `csharp/AgentQ.Desktop/Services/WorkerExecutionPipeline.cs`
  - [x] worker plan 실행 상태가 실패인데 성공처럼 반환되는 경로가 없는지 확인한다.
  - [x] partial success가 전체 success로 둔갑하지 않는지 확인한다.
  - [x] repair plan 생성/삭제 조건이 stale 상태를 남기지 않는지 확인한다.
  - [x] verification 실패가 worker plan 성공으로 덮이지 않는지 확인한다.

- [x] `csharp/AgentQ.Desktop/Services/WorkerScaffoldExecutor.cs`
  - [x] create-file/write 실패가 structured issue로 올라오는지 끝까지 확인한다.
  - [x] directory exists, file exists, parent missing, permission denied 케이스를 각각 확인한다.
    - [x] directory exists target은 실패 issue로 남긴다.
    - [x] file exists target은 skipped + 실패 issue로 남긴다.
    - [x] parent directory가 없으면 안전하게 생성한다.
    - [x] parent path가 파일이면 structured write issue로 실패한다.
    - [x] permission denied 재현/검증은 OS별 안정적인 fixture를 더 만들어야 한다.
      - [x] 기존 파일을 read-only로 만든 뒤 `OverwriteExistingFiles=true`로 overwrite를 시도해 `UnauthorizedAccessException` 계열 실패가 structured issue로 남고 생성 성공으로 기록되지 않는 것을 검증했다.
  - [x] symlink/reparse parent 경로가 모든 write 전 단계에서 차단되는지 확인한다.
  - [x] generated file list와 실제 file mutation snapshot이 일치하는지 확인한다.
  - [x] 일부 파일만 생성되고 기존 파일은 skipped 된 partial scaffold가 전체 성공으로 표시되지 않게 했다.

- [x] `csharp/AgentQ.Desktop/Services/WorkerScaffoldAutoWirer.cs`
  - [x] React/FastAPI/Rust auto-wiring target 선택이 최신 사용자 요청을 벗어나지 않는지 확인한다.
  - [x] 기존 파일을 수정하기 전에 snapshot이 반드시 남는지 확인한다.
  - [x] auto-wiring 실패가 scaffold 전체 성공처럼 보이지 않는지 확인한다.
  - [x] FastAPI auto-wiring은 관습적 `app/main.py`보다 감지된 `PythonAppRoot/main.py`를 우선한다.

- [x] `csharp/AgentQ.Desktop/Services/DesktopPlanCommandService.cs`
  - [x] plan approval 없이 실행으로 넘어가는 버튼/메서드가 없는지 확인한다.
  - [x] manual-only plan, command-only plan, modify-only plan이 scaffold 실행으로 넘어가지 않는지 확인한다.
  - [x] worker repair prompt가 사용자 draft를 덮지 않는지 확인한다.
  - [x] checkpoint/resume/generated prompt가 현재 사용자 입력을 덮지 않는지 재확인한다.
  - [x] checkpoint/session summary resume은 현재 draft가 있으면 send callback을 호출하지 않는다.

- [x] `csharp/AgentQ.Desktop/Services/DesktopVerificationCommandService.cs`
  - [x] verification fix prompt가 실패 원문과 최신 사용자 요청을 뒤섞지 않는지 확인한다.
  - [x] auto-fix와 manual fix가 서로 다른 상태를 덮지 않는지 확인한다.
  - [x] cancelled verification과 failed verification이 UI/메모리/summary에 다르게 남는지 확인한다.
  - [x] Auto Fix 승인 후 verification이 null/cancelled이면 이전 failure signature로 다음 attempt를 시작하지 않게 했다.

- [x] `csharp/AgentQ.Desktop/Services/DesktopVerificationPanelWorkflowService.cs`
  - [x] 마지막 failure state가 성공/취소/새 요청에 의해 잘못 덮이지 않는지 확인한다.
  - [x] verify command policy가 모든 진입점에서 동일하게 적용되는지 확인한다.
  - [x] output preview가 너무 길거나 민감한 내용을 저장하지 않는지 확인한다.
  - [x] verification result card preview와 workflow summary/log가 secret을 redaction하고 긴 preview line을 제한한다.

### C. DesktopAgentService 남은 세부 감사

- [x] `ExecuteToolsAsync` 전체를 다시 읽는다.
  - [x] tool result가 error인데 replay evidence가 success처럼 쓰이는 경로가 없는지 확인한다.
  - [x] tool call id가 중복/비어 있음/공백일 때 message 흐름이 깨지지 않는지 확인한다.
  - [x] tool failure 후 retry instruction이 최신 task contract를 유지하는지 확인한다.
  - [x] tool loop 중간에 transient context가 반복 삽입되지 않는지 확인한다.
  - [x] `ExecuteToolsAsync`가 빈/공백/중복 tool id를 batch-local unique id로 정규화하고 assistant tool-use 객체에도 반영한다.
  - [x] retry instruction은 현재 task contract goal과 명시 target을 포함하고, transient context는 첫 모델 요청에만 삽입된다.

- [x] final-answer consistency guard를 다시 읽는다.
  - [x] file change가 있는데 final answer가 무관할 때 항상 replacement summary가 적용되는지 확인한다.
  - [x] verification failure가 있는데 final answer가 성공처럼 말할 때 guard가 막는지 확인한다.
  - [x] local server failure가 있는데 final answer가 URL 성공처럼 말할 때 막는지 확인한다.
  - [x] scaffold failure가 있는데 final answer가 created files를 말할 때 막는지 확인한다.
  - [x] failed verification replay evidence가 있는데 최종 답변이 성공/완료처럼 보이면 deterministic failure summary로 대체한다.
  - [x] direct local-server/scaffold 경로가 실패 replay evidence를 남기면 `Run complete`도 Failed 상태로 기록한다.
  - [x] `verify_project_scaffold`의 `succeeded=false` JSON 결과를 `IsError=false`라도 실패 evidence로 취급한다.
  - [x] file-change final answer가 일반 성공 문장만 말하고 실제 변경 대상 파일/폴더를 언급하지 않으면 deterministic change summary로 대체한다.

- [x] direct deterministic fallback 경로를 다시 읽는다.
  - [x] create directory direct path가 explicit target만 생성하는지 확인한다.
  - [x] delete path direct path가 explicit target만 삭제하는지 확인한다.
  - [x] local server direct path가 session/replay/verification evidence를 모두 남기는지 확인한다.
  - [x] 직접 실행 경로와 LLM tool-loop 경로의 confidence/reporting이 일관적인지 확인한다.
  - [x] task-contract direct fallback은 routing text에서 target 추출이 실패하면 raw user text를 보조로 사용한다.
  - [x] task-contract direct fallback tool result가 실패하면 `Run complete`도 Failed 상태로 기록한다.

- [x] `BuildContextOnlyAsync`를 한 번 더 읽는다.
  - [x] memory, workspace snapshot, scaffold plan, execution lesson이 최신 요청보다 위에 서지 않는지 확인한다.
  - [x] conversation-only turn에 task-contract context가 붙지 않는지 확인한다.
  - [x] link auto-read failure가 “웹 접근 불가” 환각으로 이어지지 않는지 확인한다.
  - [x] conversation/how-to turn에는 execution lesson context가 실행 압력으로 붙지 않는다.
  - [x] link fetch 실패는 HTTP/status 실패 이유와 pasted text/local file fallback으로 표현하고, URL 접근 불가를 단정하지 말라는 rule을 포함한다.

### D. Intent / Classifier / Korean phrasing 남은 감사

- [x] `TurnIntentClassifier.cs`
  - [x] 한국어 생성/수정/삭제/실행/검증 동사가 빠진 것이 없는지 확인한다.
  - [x] “하고 싶다”, “가능할까”, “방법” 같은 consultative 표현이 Action으로 승격되지 않는지 확인한다.
  - [x] “해줘”, “만들어줘”, “돌려줘” 같은 명령형은 concrete target이 있을 때 Action이 되는지 확인한다.
  - [x] action-like example과 current command를 구분하는 테스트를 더 추가한다.
  - [x] 로그/예시/인용 안의 실행형 문장은 현재 실행 요청이 아니라 분석 대상 evidence로 분류한다.
  - [x] “검증 방법 알려줘”는 Conversation으로 유지하고, “이 변경 검증해줘”는 concrete shell Action으로 분류한다.

- [x] `UserTurnUnderstanding.cs`
  - [x] `=====` 외의 구분자, 예를 들어 `-----`, `---`, `````, quote block이 잘 분리되는지 확인한다.
  - [x] 이미지 설명 질문, “이건 무슨 뜻이지?” 같은 요청이 embedded screenshot/text를 실행하지 않는지 확인한다.
  - [x] 모델 classifier JSON이 fallback보다 위험하게 승격될 때 항상 차단되는지 확인한다.
  - [x] 모델 classifier가 action target을 path-only로 줄여도 routing text는 원래 action verb를 보존하는지 확인한다.
  - [x] dash separator로 감싼 action-looking 로그 줄과 quote block 안의 실행문은 현재 action이 아니라 embedded evidence로 남긴다.

- [x] `UserIntentTranslator.cs` / `TaskContract.cs`
  - [x] create file, create directory, delete path, run verification, run local server가 서로 섞이지 않는지 확인한다.
  - [x] shell command를 설명해달라는 요청과 실행해달라는 요청을 더 많이 테스트한다.
  - [x] “이 폴더”, “여기”, “현재 폴더” 같은 deictic word가 target 이름으로 잘못 쓰이지 않는지 추가 확인한다.
  - [x] target 추출은 첫 marker 앞 지시어에서 멈추지 않고 뒤쪽 explicit target을 찾는다.
  - [x] `dotnet test 명령 설명해줘`, `로컬서버 실행 설명해줘` 같은 설명형 요청은 실행 contract로 승격하지 않는다.
  - [x] `포트폴리오 홈페이지 만들어줘` 같은 구체적 한국어 greenfield 요청은 `CreateProject` task contract로 잡혀 scaffold plan context가 빠지지 않게 했다.

- [x] `DesktopTaskClassifier.cs`
  - [x] product review와 code review를 더 분리한다.
  - [x] feature feasibility question과 feature implementation request를 더 분리한다.
  - [x] documentation/update 요청이 실제 file write인지 설명 요청인지 확인한다.
  - [x] “만들 수 있을까/가능한가/어떨까” 같은 feature feasibility 질문은 Feature 구현 task profile로 승격하지 않는다.
  - [x] `설명` 단독 표현은 Documentation task profile로 분류하지 않고, README/docs/document target이 있을 때만 Documentation으로 분류한다.

### E. Project Memory / Execution Lesson 남은 감사

- [x] `ProjectMemoryService.cs`
  - [x] local/shared memory file merge 순서가 최신/로컬 정보를 과도하게 덮지 않는지 확인한다.
  - [x] preference/check/context fact가 query 없이 과도하게 prompt에 들어가지 않는지 확인한다.
  - [x] sensitive value 필터가 API key, bearer token, password, env var를 충분히 걸러내는지 확인한다.
  - [x] off-target 조언 필터가 너무 좁아서 다른 형태의 무관한 조언을 저장하지 않는지 확인한다.
  - [x] 반대로 실제 게임 개발/독서 앱 개발 문맥까지 잘못 제거하지 않는지 확인한다.
  - [x] shared memory와 local memory가 같은 lesson/preference/check/context fact key를 가질 때 local memory가 명시적으로 override한다.
  - [x] `x-api-key`, `access_token`, `private key`, `DATABASE_URL`, Postgres URL 같은 민감값도 prompt context와 저장 후보에서 제외한다.
  - [x] 오래 사용되지 않은 stale lesson이 최신 요청 context에 다시 섞이지 않도록 unused lesson stale 기준을 회귀 테스트와 맞췄다.

- [x] `ExecutionLessonMemoryService.cs`
  - [x] 실패 lesson이 성공 경로에서 잘못 강화되지 않는지 확인한다.
  - [x] confidence 감소/비활성화 규칙이 너무 늦거나 빠르지 않은지 확인한다.
  - [x] action intent별 lesson 선택이 최신 요청과 맞는지 확인한다.
  - [x] execution lesson이 conversation turn에 실행 압력을 주지 않는지 확인한다.
  - [x] 이번 run에서 실제로 적용되지 않은 execution lesson은 성공 결과로 강화하지 않는다.

- [x] `DesktopLearningSuggestionService.cs`
  - [x] 실패 lesson 후보가 너무 쉽게 만들어지지 않는지 확인한다.
  - [x] provider/model failure lesson이 민감한 error detail을 저장하지 않는지 확인한다.
  - [x] user가 직접 저장하기 전 pending lesson이 prompt context에 들어가지 않는지 확인한다.
  - [x] pending failure lesson title/content/source에서 API key, bearer token, password, access token, database URL을 redaction한다.

### F. Provider / Core 남은 감사

- [x] `csharp/AgentQ.Core/Models/ChatModels.cs`
  - [x] role/content model이 provider별 변환에서 손실되지 않는지 확인한다.
    - [x] OpenAI는 Core `User` + `ToolResult`를 `role=tool` 메시지로, Anthropic은 user content block `tool_result`로 변환하는 것을 request body tests로 확인했다.
  - [x] tool result가 user role로 들어가는 구조가 모든 provider와 호환되는지 확인한다.
    - [x] OpenAI multiple tool results expansion과 Anthropic blank tool result id drop을 focused tests로 확인했다.
  - [x] compacted message가 tool-call protocol을 깨지 않는지 확인한다.
    - [x] `ConversationCompactor`가 최근 tail 경계를 assistant tool-use/user tool-result 사이에서 자르지 않도록 보정했다.
  - [x] provider에서 온 tool id/name/result id 앞뒤 공백이 tool protocol과 tool lookup을 깨지 않는지 확인한다.
    - [x] `ChatContent.CreateToolUse` / `CreateToolResult`가 provider 식별자를 trim하도록 수정하고 `ChatContent_ToolFactoriesNormalizeProviderIdentifiers`로 검증했다.

- [x] `ToolCallDeltaBuffer.cs`
  - [x] multiple tool calls, partial arguments, duplicate index, missing id/name 케이스를 더 확인한다.
    - [x] 기존 `ProviderUnitTests`가 다중 pending call 순서, partial argument 조립, provider id 누락 fallback, tool name 누락/drop, whitespace id/name drop을 커버하는 것을 재확인했다.
  - [x] malformed JSON argument가 provider stream chunk에서 Desktop/CLI tool execution까지 가지 않고 실행 전 tool error로 차단되는지 확인한다.
  - [x] streaming tool call id/name delta의 앞뒤 공백이 그대로 실행 계층까지 전달되지 않는지 확인한다.
    - [x] `ToolCallDeltaBuffer.SetToolId` / `SetToolName`이 trim하도록 수정하고 `ToolCallDeltaBuffer_TrimsProviderToolIdentifiers`로 검증했다.

- [x] `OpenAiCompatibleProvider.cs`
  - [x] Chat Completions와 OpenAI-compatible provider가 tool call schema를 다르게 주는 케이스를 확인한다.
    - [x] current `tool_calls`, legacy `function_call`, invalid current tool_calls + valid legacy fallback, streaming legacy function_call을 provider tests로 확인했다.
  - [x] streaming/non-streaming response에서 reasoning/text/tool-use가 섞일 때 누락이 없는지 확인한다.
    - [x] non-stream text + multiple tool calls + usage, streaming reasoning_content + text + tool-use delta를 focused tests로 확인했다.
  - [x] tool-call argument가 object/string/empty/malformed일 때 behavior를 통일한다.
    - [x] outbound assistant tool-use history에서 malformed/string/non-object input은 OpenAI `function.arguments`에 그대로 재주입하지 않고 `{}`로 정규화한다.
    - [x] 공백 tool id/name과 공백 tool result id는 OpenAI request의 invalid tool protocol message로 보내지 않는다.

- [x] `AnthropicProvider.cs`
  - [x] content block start/delta/stop 순서 변형을 더 확인한다.
    - [x] malformed event, comment/heartbeat, multi-line data, text block start/delta/stop, message_delta 후 message_stop 흐름을 provider tests로 확인했다.
  - [x] tool result id가 비거나 공백일 때 안전하게 drop되는지 확인한다.
    - [x] request body 캡처 테스트로 공백 `tool_use_id` tool result가 Anthropic content block에 재주입되지 않는 것을 고정했다.
  - [x] usage 누락/부분 usage가 run workflow에 문제를 일으키지 않는지 확인한다.
    - [x] non-stream usage는 cache input token을 input token에 합산한다.
    - [x] stream `message_start`/`message_delta`의 부분 usage를 병합해 `message_stop`에서 한 번만 actual usage chunk로 내보낸다.

- [x] provider configuration
  - [x] timeout, max token, base URL, model, vision-review 설정이 UI/CLI/env/file 사이에서 일관적인지 확인한다.
    - [x] CLI explicit `--timeout 60` / `--max-tokens 4096`이 env fallback 값에 덮이는 버그를 수정했다.
  - [x] 보호 저장/로드가 설정을 누락하지 않는지 확인한다.
    - [x] provider/model/base URL/API key, embedding 설정, timeout/max tokens, desktop context/link/vision/work mode/max tool steps/UI language 라운드트립을 테스트로 고정했다.

### G. Tools 남은 감사

- [x] `BashTool.cs`
  - [x] exit code, stdout/stderr, timeout, working directory, JSON result 형식이 모든 caller에서 일관되게 해석되는지 확인한다.
  - [x] destructive command guard가 PowerShell/CMD/Git 변형을 충분히 막는지 확인한다.
  - [x] command output 안의 prompt injection을 Agent Q가 지시로 따르지 않게 context wording을 확인한다.
    - [x] Desktop/CLI system prompt에 shell output, tool results, logs, compiler/test output, file contents를 untrusted evidence로 취급하라는 rule을 추가했다.

- [x] `ReadFileTool.cs`
  - [x] symlink/reparse outside file을 읽지 않는지 확인한다.
  - [x] 큰 파일, binary file, encoding 문제를 안전하게 처리하는지 확인한다.
    - [x] `ReadAllLines` 전체 메모리 로드를 스트리밍 line window 읽기로 바꾸고, NUL byte binary preflight를 추가했다.
  - [x] line range가 잘못된 경우 structured error를 반환하는지 확인한다.

- [x] `WriteFileTool.cs` / `EditFileTool.cs`
  - [x] path boundary와 approval policy가 Desktop과 CLI에서 일관적인지 확인한다.
  - [x] overwrite, create new, empty file, encoding, newline 처리 방식을 확인한다.
    - [x] 기존 UTF-16/BOM 텍스트 파일을 `edit_file`/`write_file`로 수정할 때 UTF-8로 몰래 변환하지 않고 기존 인코딩을 보존한다.
    - [x] 기존 binary/NUL 파일은 텍스트 write/edit 대상으로 취급하지 않고 거부한다.
  - [x] edit 실패가 partial write를 남기지 않는지 확인한다.
    - [x] write/edit 저장을 같은 디렉터리 temp file 작성 후 replace/move로 바꿔 원본 truncation 위험을 줄였다.

- [x] `ListDirectoryTool.cs`
  - [x] hidden file, reparse directory, large directory paging을 확인한다.
    - [x] reparse/symlink entry는 대상 metadata를 따라가지 않고 `isReparsePoint=true`, `sizeBytes=null`로 보고한다.
  - [x] directory listing 결과가 너무 커서 prompt를 오염시키지 않는지 확인한다.
    - [x] requested limit은 500개로 clamp되고 `limitReached`/`requestedLimit` evidence를 남긴다.

- [x] `GrepTool.cs` / `GlobTool.cs`
  - [x] explicit no-follow traversal이 모든 OS에서 동작하는지 확인한다.
  - [x] huge result cap, binary skip, ignored directory 처리 방식을 확인한다.
    - [x] `GrepTool` file cap은 2000개 exactly일 때 잘림으로 과장하지 않고, 2001개 이상일 때만 `fileLimitReached=true`로 보고한다.
    - [x] `GrepTool`은 확장자만이 아니라 NUL byte sample로 binary file을 skip한다.

- [x] `WebSearchTool.cs`
  - [x] max_results clamp, query validation, network failure reporting을 확인한다.
    - [x] overlong query는 HTTP 요청 전에 structured error로 거부한다.
  - [x] search result가 최신 사용자 요청과 무관한 context로 과하게 들어가지 않는지 확인한다.
    - [x] title/url/snippet을 길이 제한으로 정규화하고, 결과 JSON 계약을 `title`/`url`/`snippet` 소문자 속성으로 고정했다.

- [x] Tools 공용 계약/guard/helper 파일
  - [x] `ToolRegistry.cs`, `ITool.cs`, `IPermissionEnforcer.cs`, `PluginEchoTool.cs`의 깨진 XML 주석을 정리했다.
  - [x] `ToolPathGuard.cs`가 invalid path syntax를 예외로 흘리지 않고 structured error로 반환하게 했다.
  - [x] `CreateDirectoryTool.cs` / `DeletePathTool.cs` / `TextFileIo.cs` / `EditRiskGuard.cs` / `ToolPathGuard.cs`를 직접 읽고 path boundary, binary/text, high-risk edit guard 흐름을 확인했다.

### H. CLI 남은 감사

- [x] `CliToolLoopRunner.cs`
  - [x] Desktop과 같은 no-tool guard/task-contract failure 규칙을 갖는지 확인한다.
    - [x] non-interactive action prompt에서 모델이 허용된 mutation tool 없이 완료를 주장하면 retry 후에도 실패로 기록한다.
  - [x] max step stop이 automation success로 보고되지 않는지 추가 확인한다.
  - [x] malformed streamed tool input은 permission prompt와 tool execution 전에 tool error로 처리한다.

- [x] `CliNonInteractiveRunner.cs`
  - [x] automation output schema가 success/failure를 명확히 구분하는지 확인한다.
  - [x] bash non-zero exit code, tool error, guard stop이 모두 failure로 전달되는지 확인한다.
  - [x] 한국어 action/completion/manual fallback 감지 문자열이 mojibake로 깨져 no-tool completion guard를 우회하지 않는지 확인한다.
    - [x] `수정`, `고쳐`, `만들`, `생성`, `삭제`, `작성`, `구현`과 `완료`, `수정했`, `고쳤`, `생성했`, `만들었`, `구현했` 정상 한글 키워드로 교체하고 한국어 no-tool completion 회귀 테스트를 추가했다.

- [x] CLI configuration/session/history 보조 파일
  - [x] `CliConfigurationLoader.cs`가 저장 설정을 병합할 때 명시 CLI 기본값 `--timeout 60`, `--max-tokens 4096`을 persisted config로 덮지 않게 했다.
  - [x] `ChatConversationHistory.cs` / `ConversationCompactor.cs`가 compact tail 경계를 assistant tool-use와 user tool-result 사이에서 자르지 않게 했다.
  - [x] `CliApplication.cs`, `CliInteractive*`, `AutomationSupport.cs`, `SessionStore.cs`, `StreamingProcessor.cs`, `ToolCapabilitySnapshot.cs`, `Program.cs`, `AgentQ.Cli.csproj`를 직접 읽고 non-interactive/interactive 실행 경계를 확인했다.

- [x] `CliInteractiveToolCommands.cs`
  - [x] `/run` direct tool call이 Desktop permission policy와 너무 다르지 않은지 확인한다.
  - [x] malformed JSON이 permission prompt를 열지 않는지 추가 확인한다.

- [x] `ConsolePermissionEnforcer.cs`
  - [x] approval reuse가 의도한 tool에만 적용되는지 확인한다.
  - [x] external write, destructive, network, shell, project write risk level 문구가 명확한지 확인한다.
    - [x] CLI permission prompt summary에 shell/network/project write/edit/create/delete risk line을 추가했다.

### I. UI 이벤트 흐름 남은 감사

- [x] `MainWindow.xaml.cs`
  - [x] 모든 버튼 이벤트가 사용자 draft를 덮지 않는지 확인한다.
    - [x] Stop local server 버튼은 draft가 있으면 실행 prompt로 `InputText`를 덮지 않고 보존/차단한다.
  - [x] Continue, Resume checkpoint, Resume summary, Fix verification, Auto fix가 서로 상태를 잘못 공유하지 않는지 확인한다.
  - [x] busy 상태에서 새 요청이 들어올 때 stop/redirect 흐름이 안전한지 확인한다.

- [x] `MainViewModel.cs`
  - [x] status color/accent가 실패를 성공처럼 보이지 않게 하는지 확인한다.
    - [x] `not complete`, `not saved`, `not verified`, `not executed` 같은 부정 완료 상태가 초록 완료색으로 보이지 않게 했다.
  - [x] `CanExecuteWorkerScaffold`, `CanContinueLastRun`, `CanResume...` 계산이 stale 상태를 남기지 않는지 확인한다.
    - [x] busy 상태에서는 Continue/Resume checkpoint/Resume session summary를 비활성화한다.
    - [x] `CurrentWorkerExecutionContext` 교체 시 worker scaffold/repair CanExecute 속성 변경을 알린다.
  - [x] file change review status가 Git commit 승인 상태와 일관적인지 확인한다.

- [x] panel view models
  - [x] Agent council, run timeline, verification panel, git panel, memory panel이 실행 상태를 과장하지 않는지 확인한다.
    - [x] Project panel reset이 이전 분석의 Ready/green/count/list 상태를 남기지 않도록 collections/counts/accent를 모두 초기화한다.
  - [x] confidence Low/Medium/High 문구가 실제 evidence와 맞는지 확인한다.

- [x] view code-behind / XAML event binding
  - [x] `Views/*Panel.xaml.cs`와 `CodePreviewWindow.xaml.cs`를 직접 읽고, 대부분 이벤트를 `MainWindow` command service로 전달하는 얇은 브리지임을 확인했다.
  - [x] `Click`, `SelectionChanged`, `PasswordChanged`, `PreviewKeyDown`, `PreviewMouseWheel`, `ScrollChanged`, `SelectedItemChanged` XAML event binding을 검색해 code-behind 핸들러와 대응되는지 확인했다.

### J. 문서 / 인코딩 / 정리

- [x] `docs/Agent Q.md`를 현재 architecture와 맞춘다.
  - [x] deterministic Desktop service 우선 원칙을 반영한다.
  - [x] LLM은 reasoning/repair/explanation에 집중한다는 원칙을 반영한다.
  - [x] lesson memory는 긴 대화 저장소가 아니라 행동 규칙 저장소라는 점을 반영한다.
  - [x] mojibake가 있던 기존 문서를 UTF-8 architecture note로 재작성했다.

- [x] 깨진 한글(mojibake)이 남아 있는 테스트 이름, 주석, 문서 항목을 정리한다.
  - [x] 사용자-facing 문구는 UTF-8 한글로 유지한다.
  - [x] source code string literal에서 encoding 위험이 있으면 `\u` escape를 사용한다.
  - [x] 의미가 불확실한 깨진 문장은 임의 복원하지 말고 새 테스트 문장으로 대체한다.
  - [x] `ToolAndConfigurationTests.cs`의 의미 불확실한 깨진 XML 주석은 복원하지 않고 제거했다.
  - [x] 대표 mojibake 패턴(`筌`, `癰`, `濡`, `沅`, `諛`, `獄`, `野`, `揶`, `嚥`, `袁` 등)이 `csharp`/`docs` 범위에서 더 이상 검색되지 않음을 확인했다.

- [x] 삭제된 docs 파일이 의도된 삭제인지 확인한다.
  - [x] `docs/hermes-agent-porting.ko.md`
  - [x] `docs/hermes-import-notes.md`
  - [x] `docs/superpowers/plans/2025-04-07-pork-damage-flash.md`
  - [x] 현재 git tracked 파일이 아니며 이번 WIP 삭제 상태가 아님을 `git ls-files`/`git status --short`로 확인했다.

- [x] 새로 생긴 문서 파일이 필요한지 확인한다.
  - [x] `docs/DEVELOPMENT_PLAN.md`
  - [x] `docs/TODO.md`
  - [x] 두 파일 모두 git tracked이며 현재 감사/TurnState 개발 계획 문서로 유지 필요함을 확인했다.

### K. 남은 위험 / 의도적으로 미룬 UX 개선

- [x] 남은 위험과 의도적으로 미룬 UX 개선을 분리 기록한다.
  - [x] `csharp/AgentQ.Desktop/Services`는 핵심 실행 경계 파일을 많이 감사했지만, 현재 파일 수가 매우 많아 최종 “전체 파일 직접 읽음” 조건은 아직 닫지 않는다.
  - [x] `csharp/AgentQ.Tests` 전체 coverage는 full test green evidence가 있지만, 요구사항별 coverage matrix로 직접 대조하는 작업은 아직 남아 있다.
  - [x] 현재 e2e는 service-level deterministic tests이며, 실제 WPF UI 클릭/렌더링 end-to-end 자동화까지는 수행하지 않았다.
  - [x] permission denied fixture는 Windows read-only overwrite로 안정화했지만, Linux/macOS ACL별 동작은 별도 플랫폼 검증으로 남긴다.
  - [x] UX 개선은 이번 감사 범위에서 실행 경계/최신 요청 보존/검증 진실성보다 낮은 우선순위로 유지한다.

- [x] `csharp/AgentQ.Desktop/Services` 50줄 이하 DTO/enum/보조 파일과 51-120줄대 일부 보조 서비스를 이어서 직접 읽었다.
  - [x] `DesktopUsageSnapshot.DisplayText`와 `DesktopToolCallbacksFactory`의 한국어 UI 문구가 실제 소스에서는 정상 UTF-8임을 `git diff`로 확인하고, PowerShell 출력 인코딩 오판으로 생긴 영어 변경을 정상 한국어로 복원했다.
  - [x] Services 소스에 알려진 mojibake UI 조각이 다시 들어오면 실패하는 회귀 테스트를 추가했다.

- [x] `csharp/AgentQ.Desktop/Services` 121-220줄대 실행 보조 서비스를 이어서 직접 읽었다.
  - [x] `PlaywrightVerificationArtifactCollector`가 `cmd /c cd /d "front end" && ...` 형태의 Windows directory-scoped Playwright command에서 report/screenshot artifact를 놓치던 경로를 수정했다.
  - [x] `LinkContentFetcher.FetchAsync`가 malformed URL 후보를 `new Uri(...)`에서 throw하지 않고 `InvalidUrl` structured result로 반환하게 했다.
  - [x] `TaskExecutor`와 `MultiAgentOrchestrator`가 외부에서 들어온 `TaskPlan.VerificationCommand`를 prompt에 넣기 전에 `VerificationCommandPolicy` allowlist로 다시 필터링하게 했다.
  - [x] 관련 회귀 테스트를 추가하고 focused test 26개 및 Desktop build로 검증했다.

- [x] `csharp/AgentQ.Desktop/Services` 221-360줄대 context/index/scaffold 보조 서비스를 이어서 직접 읽었다.
  - [x] `DesktopSourceBrowserService`, `WorkspaceIndexer`, `EmbeddingIndexBuilder`, `CSharpRoslynAnalysisService`, `ExecutionLessonMemoryService`, `DesktopLearningSuggestionService`, `DesktopAutoFixWorkflowService`, `DesktopScaffoldIntentRouter`, `WorkerScaffoldAutoWirer`를 직접 읽었다.
  - [x] `EmbeddingIndexBuilder`가 `.agentq`, `.agents`, `.codex`, `.codex-build` 내부 세션/체크포인트/메모리 파일을 임베딩 chunk로 색인할 수 있던 오염 경로를 차단했다.
  - [x] `DesktopSourceBrowserService`가 AgentQ/Codex 내부 메타데이터 폴더를 source browser에 사용자 파일처럼 노출하지 않게 했다.
  - [x] `CSharpRoslynAnalysisService`가 AgentQ/Codex 내부 메타데이터와 symlinked directory를 C# 코드 분석 결과로 끌어오지 않게 했다.
  - [x] `DesktopScaffoldIntentRouter`가 `.agentq`/`.agents`/`.codex`/`.codex-build`만 있는 workspace를 기존 프로젝트로 오판하지 않게 했다.
  - [x] 관련 회귀 테스트를 추가하고 focused test 18개로 검증했다.

- [x] Git/MCP service 경계를 이어서 직접 읽었다.
  - [x] `DesktopGitService`, `DesktopGitPanelWorkflowService`, `DesktopGitCommandService`, `DesktopGitWorkflowService`, `McpBridgeTool`, `McpServerRegistry`, `StdioMcpClient`, `McpServerConfig`, `McpToolName`, `McpToolInfo`를 직접 읽었다.
  - [x] Git status/diff/changed-file 목록이 `.agents`, `.codex`, `.codex-build` 내부 메타데이터를 code review/commit summary prompt나 staging 목록에 섞지 않도록 `.agentq`와 같은 제외 규칙을 적용했다.
  - [x] MCP prompt context가 실제 tool registration과 같은 workspace safety filter를 쓰도록 `McpServerRegistry.BuildContext(config, workspaceRoot)` 경로를 적용했다.
  - [x] 관련 회귀 테스트를 추가하고 MCP/Git focused test 9개 및 Desktop build로 검증했다.

- [x] Project memory / search / symbol / dependency graph 경계를 이어서 직접 읽었다.
  - [x] `ProjectMemoryService`, `DesktopHybridSearchTool`, `DesktopSemanticSearchTool`, `DesktopSymbolSearchTool`, `WorkspaceSymbolIndexService`, `WorkspaceAnalysisService`, `WorkspaceDependencyGraphService`를 직접 읽었다.
  - [x] symbol index, dependency graph, hybrid keyword scan, semantic/hybrid stored chunk 결과가 `.agentq`, `.agents`, `.codex`, `.codex-build`, `.agentq-verify` 내부 메타데이터를 검색 후보로 돌려주지 않게 했다.
  - [x] 이미 오염된 embedding chunk가 저장되어 있어도 `semantic_search`와 `hybrid_search` 결과에서 내부 메타 path를 제외하도록 방어선을 추가했다.
  - [x] 관련 회귀 테스트를 추가하고 search/symbol/dependency focused test 14개 및 Desktop build로 검증했다.

- [x] Prompt assembly / run workflow / verification / worker execution 경계를 이어서 직접 읽었다.
  - [x] `DesktopPromptAssemblyService`, `DesktopPromptBuilder`, `DesktopAgentRunWorkflowService`, `DesktopVerificationWorkflowService`, `DesktopVerificationRunner`, `DesktopVerificationCommandService`, `DesktopVerificationPanelWorkflowService`, `WorkerExecutionPipeline`, `WorkerScaffoldExecutor`, `WorkerPlanValidator`를 직접 읽었다.
  - [x] `WorkerPlanValidator`가 symlink/reparse directory를 통해 workspace 밖으로 resolve되는 file mutation path를 approval preview 단계에서도 blocker로 표시하게 했다.
  - [x] 실행 단계의 `WorkerScaffoldExecutor` resolved path guard와 validator preview guard가 같은 방향을 보도록 회귀 테스트를 추가했다.
  - [x] 관련 worker/verification focused test 55개 및 Desktop build로 검증했다.

- [x] Worker host / screenshot visual evidence 경계를 이어서 직접 읽었다.
  - [x] `NativeWorkerHost`, `PythonWorkerHost`, `TypeScriptWorkerHost`, `NativeWorkerModels`, `PythonWorkerModels`, `TypeScriptWorkerModels`, `WorkerScaffoldContext`, `ScreenshotEvidenceQualityChecker`, `ScreenshotLlmVisionEvidenceBuilder`, `ScreenshotLlmVisionReviewer`, `ScreenshotVisualHeuristicEvaluator`, `ScreenshotVisualReviewService`, `DesktopScreenshotLlmVisionWorkflowService`를 직접 읽었다.
  - [x] screenshot artifact quality/visual review 경로가 symlinked directory를 통해 workspace 밖 파일을 읽지 않도록 `WorkspacePathResolver.IsResolvedInsideWorkspace`를 적용했다.
  - [x] 관련 screenshot/visual evidence focused test 10개 및 Desktop build로 검증했다.

- [x] Config / attachment / file-change / diagnostics / telemetry 보조 서비스를 이어서 직접 읽었다.
  - [x] `DesktopConfigService`, `DesktopAttachmentWorkflowService`, `DesktopAttachmentSelectionService`, `DesktopFileChangeReviewService`, `FileMutationSnapshotService`, `DesktopDiagnosticsService`, `DesktopTelemetryService`, `SensitiveTextRedactor`, `DesktopTelemetryEvent`를 직접 읽었다.
  - [x] diagnostics는 이미 redaction을 적용하고 있었지만 telemetry detail은 그대로 저장하고 있어, `DesktopTelemetryService`가 저장 직전에 `SensitiveTextRedactor`를 적용하도록 수정했다.
  - [x] 관련 redaction/file-change focused test 11개 및 Desktop build로 검증했다.

- [x] `DesktopAgentService.cs` 후반부를 끝까지 다시 직접 읽었다.
  - [x] scaffold primary execution, scaffold verification fallback, provider request context assembly, routed user message, attachment 처리, `ExecuteToolsAsync`, replay 저장, direct create/delete fallback, file mutation snapshot, tool registry/MCP 등록 경계를 읽었다.
  - [x] `verify_project_scaffold`가 `ToolResult.Success`로 `succeeded=false` JSON을 반환할 때도 `executedCommands`에 verification command를 기록할 수 있어, `DesktopVerificationSelector`/confidence가 실패한 검증을 “이미 실행된 검증”으로 오인할 수 있던 경로를 차단했다.
  - [x] `verify_project_scaffold`는 JSON 결과의 `succeeded=true`일 때만 executed verification command로 기록하고, 실패/비정상 JSON은 verification satisfied evidence로 쓰지 않게 했다.
  - [x] 관련 DesktopAgentService/DesktopVerificationSelector focused test 9개 및 Desktop build로 검증했다.

- [x] Plan/checkpoint/workspace command/model routing/confidence/verification evidence 보조 경계를 이어서 직접 읽었다.
  - [x] `DesktopWindowCommandService`, `DesktopWorkspaceCommandService`, `DesktopWorkspaceContextWorkflowService`, `DesktopStartupCommandService`, `DesktopModelRoutingAdvisor`, `DesktopModelRoutingRecommendation`, `DesktopModelRoutingTier`, `DesktopProviderFailureDescription`, `DesktopProviderModelCatalog`, `DesktopProviderModelDiscoveryService`, `DesktopPlanWorkflowService`, `DesktopPlanCheckpointWorkflowService`, `DesktopPlanApprovalPreviewService`, `DesktopPlanParser`, `AgentCheckpointService`, `DesktopCheckpointWorkflowService`, `AgentSessionSummaryService`, `DesktopSessionSummaryBuilder`, `DesktopConversationSummaryBuilder`, `ConversationCompactor`, `DesktopConfidenceAssessor`, `DesktopConfidenceAssessment`, `DesktopEvidenceFormatter`, `VerificationResultCard`, `VerificationRunResult`, `ShellVerificationResultDetector`, `VerificationArtifact`, `VerificationArtifactEvidenceBuilder`, `VerificationCommandPolicy`, `VerificationFailureAnalysis`, `VerificationFailureClassifier`, `VerificationFailureKind`를 직접 읽었다.
  - [x] `DesktopConfidenceAssessor`가 `npm run build; ...`처럼 build/test marker를 포함한 unsafe command를 “build or test verification ran” 신뢰도 신호로 계산할 수 있던 경로를 차단했다.
  - [x] confidence assessor는 shell separator/destructive token이 없는 안전한 executed verification command만 검증 evidence로 점수화한다.
  - [x] 관련 confidence/selector/DesktopAgentService focused test 16개 및 Desktop build로 검증했다.

- [x] MCP/permission/replay/embedding 저장 경계를 이어서 직접 읽었다.
  - [x] `McpServerConfig`, `McpToolInfo`, `McpToolName`, `McpBridgeTool`, `IMcpClient`, `StdioMcpClient`, `PermissionRiskLevel`, `ToolPermissionAssessment`, `ToolPermissionDecision`, `ToolPermissionPolicyResult`, `ToolPermissionClassifier`, `ToolPermissionPolicy`, `DesktopPermissionEnforcer`, `PermissionApprovalDialog`, `EmbeddingIndexStore`, `EmbeddingIndexPaths`, `EmbeddingIndexManifest`, `EmbeddingIndexChunk`, `EmbeddingIndexBuildResult`, `OpenAiEmbeddingClient`, `IEmbeddingClient`, `DesktopEmbeddingClientFactory`, `ProjectAgentConfigService`, `ProjectAgentConfig`, `ProjectMemory`, `ProjectMemoryGc`, `ProjectMemoryGcService`, `EvalReplayDashboardService`, `ToolReplayService`, `ToolReplayEntry`, `ToolReplaySession`를 직접 읽었다.
  - [x] `PermissionApprovalDialog`의 한국어 권한 버튼/라벨이 mojibake로 남아 있어 승인 의미가 흐려지던 user-control UI 경로를 정상 UTF-8 한글로 고쳤다.
  - [x] `DesktopAgentService`와 `DesktopSessionSummaryBuilder`의 off-target assistant 감지 helper에 남아 있던 mojibake 문자열을 정상 한글/영문 감지 패턴으로 교체했다.
  - [x] Services mojibake guard test를 `嫄`, `誘`, `鍮`, `寃`, `臾`, `野`, `놁` 조각까지 잡도록 보강했다.
  - [x] 관련 permission/mojibake focused test 66개 및 Desktop build로 검증했다.

- [x] 권한/프롬프트/worker plan 보조 경계를 이어서 재확인했다.
  - [x] `ToolPermissionClassifier`, `ToolPermissionPolicy`, `DesktopPermissionEnforcer`, `PermissionApprovalDialog`, `DesktopLocalizer`, `DesktopText`, `EvalReplayDashboardService`, `DesktopToolCallbacksFactory`, `DesktopPromptAssemblyService`, `DesktopPromptBuilder`, `DesktopGeneratedPromptGuard`, `TaskContextSelector`, `AgentPlanWorkerPlanAdapter`, `WorkerPlanValidator`, `WorkerPlanCandidateBuilder`, `WorkerPlanPreviewBuilder`, `WorkerPlanApprovalSummaryBuilder`, `WorkerPlan`를 작은 범위로 다시 직접 읽었다.
  - [x] `DesktopToolCallbacksFactory`의 max-step 연장 확인 메시지가 깨져 있으면 사용자가 실행 연장/중단 의미를 판단하기 어려우므로 정상 UTF-8 한국어로 복구했다.
  - [x] `rg`로 `csharp/AgentQ.Desktop/Services`의 알려진 mojibake 조각이 더 이상 남지 않는지 확인했다.
  - [x] 관련 permission/mojibake focused test 66개와 Desktop build로 다시 검증했다.

- [x] run workflow / auto-fix / verification runner 경계를 이어서 직접 읽었다.
  - [x] `DesktopAgentRunWorkflowService`, `DesktopAutoFixWorkflowService`, `DesktopVerificationWorkflowService`, `DesktopVerificationRunner`, `WorkspacePathResolver`를 직접 읽었다.
  - [x] `DesktopVerificationRunner`가 검증 후 `.agentq-verify`를 정리할 때 symlink/reparse directory를 recursive delete할 수 있던 경로를 차단했다.
  - [x] `.agentq-verify`가 workspace 밖으로 향하는 symlink이면 cleanup을 건너뛰고 외부 target을 보존하는 회귀 테스트를 추가했다.
  - [x] 관련 verification runner/mojibake focused test 2개 및 Desktop build로 검증했다.

- [x] workspace context / analysis / source browser 경계를 이어서 직접 읽었다.
  - [x] `WorkspaceAnalysisService`, `DesktopWorkspaceAnalysisReportBuilder`, `VisualEvidenceService`, `DesktopSourceBrowserService`, `WorkspaceDependencyGraphService`, `WorkspaceSymbolIndexService`, `WorkspaceIndexer`를 직접 읽었다.
  - [x] `WorkspaceIndexer`가 `.agentq-verify` 검증 산출물을 workspace context snapshot에 포함할 수 있던 오염 경로를 차단했다.
  - [x] `WorkspaceAnalysisService`가 `.agentq` 내부 config/memory 파일을 project map/key files로 노출할 수 있던 오염 경로를 제거했다.
  - [x] 관련 workspace analysis/indexer/verification runner focused test 3개 및 Desktop build로 검증했다.

- [x] scaffold planner / task contract completion 경계를 이어서 직접 읽었다.
  - [x] `ProjectScaffoldPlanner`, `TaskContract`, `WorkspaceDependencyGraphService`, `WorkspaceSymbolIndexService`의 남은 구간을 직접 읽었다.
  - [x] `ProjectScaffoldPlanner`의 bare-project clarifying question은 정상 한국어 소스였고, PowerShell 출력 인코딩으로만 mojibake처럼 보였음을 `rg`와 focused test로 재확인했다.
  - [x] `TaskContractCompletionChecker`가 `SearchAndSummarize`에서 search/fetch/read 도구를 실제 실행하지 않고도 "검색 도구 없음/접근 불가" 제한 보고를 완료로 받아줄 수 있던 우회를 차단했다.
  - [x] 관련 scaffold clarifying/search contract focused test 4개, Tests build, Desktop build로 검증했다.

- [x] workspace config / memory / checkpoint 저장 경계를 이어서 직접 읽었다.
  - [x] `WorkerScaffoldTemplateRenderer`, `ProjectMemoryService`, `DesktopWorkspaceContextWorkflowService`, `AgentSessionSummaryService`, `DesktopCheckpointWorkflowService`, `AgentCheckpointService`, `DesktopPlanCheckpointWorkflowService`, `ProjectAgentConfigService`를 직접 읽었다.
  - [x] `.agentq/config.json`이 symlink/reparse `.agentq` directory를 통해 workspace 밖 config를 읽거나 쓸 수 있던 경로를 차단했다.
  - [x] unsafe config path는 load 시 없는 config처럼 처리하고, save 시 명시 실패하도록 했다.
  - [x] 관련 ProjectAgentConfigService/TaskContract focused test 4개, Tests build, Desktop build로 검증했다.

- [x] `.agentq` storage / evidence write 경계를 이어서 직접 읽었다.
  - [x] `ProjectMemoryService`, `ToolReplayService`, `DesktopTelemetryService`, `DesktopDiagnosticsService`, `DesktopLocalServerService`의 `.agentq` load/save/write 구간을 직접 읽었다.
  - [x] `.agentq/memory.local.json`, `.agentq/memory.shared.json`이 symlink/reparse `.agentq` directory를 통해 workspace 밖 memory를 prompt context로 가져오거나 local lesson을 밖에 저장할 수 있던 경로를 차단했다.
  - [x] `.agentq/replay`, `.agentq/telemetry`, `.agentq/diagnostics`, `.agentq/local-server`가 symlink/reparse `.agentq` directory를 통해 외부 evidence/session/log를 읽거나 쓸 수 있던 경로를 차단했다.
  - [x] 관련 symlink guard focused test 6개, Tests build, Desktop build로 검증했다.

- [x] execution lesson / mutation snapshot / large tool output 저장 경계를 이어서 직접 읽었다.
  - [x] `ExecutionLessonMemoryService`, `FileMutationSnapshotService`, `DesktopAgentService.TrySaveFullToolOutput`를 직접 읽었다.
  - [x] `.agentq/lessons`가 symlink/reparse `.agentq` directory를 통해 외부 execution lesson을 prompt context로 가져오거나 lesson event를 밖에 저장할 수 있던 경로를 차단했다.
  - [x] `.agentq/snapshots`와 `.agentq/tool-output`이 symlink/reparse `.agentq` directory를 통해 workspace 밖으로 mutation snapshot/full tool output을 저장할 수 있던 경로를 차단했다.
  - [x] 관련 symlink guard focused test 5개, Tests build, Desktop build로 검증했다.

- [x] system skill / embedding index 저장-조회 경계를 이어서 직접 읽었다.
  - [x] `SystemSkillService`, `EmbeddingIndexPaths`, `EmbeddingIndexStore`를 직접 읽었다.
  - [x] `.agentq/skills`와 `.agentq/embeddings`가 symlink/reparse `.agentq` directory를 통해 workspace 밖 skill/index/chunks를 prompt/search context로 가져오거나 밖에 저장할 수 있던 경로를 차단했다.
  - [x] 관련 symlink guard focused test 4개, Tests build, Desktop build로 검증했다.

- [x] Git panel / branch recovery 경계를 이어서 직접 읽었다.
  - [x] `DesktopGitService`, `DesktopGitCommandService`, `DesktopGitPanelWorkflowService`, `DesktopGitWorkflowService`, `GitBranchRecoveryAnalyzer`, `GitBranchStatusAnalyzer`, `GitPullSafetyAnalyzer`, `GitChangedFile`, `GitCommandResult`, `DesktopGitSnapshot`을 직접 읽었다.
  - [x] git status rename 표기에서 목적지나 원본이 `.agentq`, `.agents`, `.codex`, `.codex-build` 내부 메타 path인 변경이 Git panel 변경 목록에 남을 수 있던 경로를 차단했다.
  - [x] Git panel stage/unstage/file diff API가 내부 AgentQ/Codex 메타 path를 받으면 git 명령을 실행하지 않고 실패로 돌려주도록 했다.
  - [x] 관련 GitService focused test 4개, Tests build, Desktop build로 검증했다.

- [x] task decomposition / generated prompt / search retry 보조 경계를 이어서 직접 읽었다.
  - [x] `DesktopGeneratedPromptGuard`, `DesktopSearchRetryService`, `TaskDecomposer`, `TaskExecutor`, `TaskContextSelector`, `AutoFixLoopGuard`, `FailureFingerprintService`와 `DesktopAgentService`의 task decomposition 호출부를 직접 읽었다.
  - [x] task decomposition 실행 결과가 실패했는데도 `Execution Completed` 문장으로 반환되어 UI run completion이 성공으로 분류할 수 있던 경로를 차단했다.
  - [x] task decomposition 실패 문장을 `run_task_decomposition_failed`로 분류하고, 성공 문장과 분리했다.
  - [x] 관련 task decomposition/GitService focused test 11개, Tests build, Desktop build로 검증했다.

- [x] UI helper / localizer / usage 표시 경계를 이어서 직접 읽었다.
  - [x] `DesktopClipboardService`, `DesktopCodeHighlighter`, `DesktopCodePreviewWindowService`, `DesktopPanelEventBinder`, `DesktopText`, `DesktopLocalizer`, `DesktopUsageSnapshot`, `DesktopUsageTracker`를 직접 읽었다.
  - [x] `DesktopUsageSnapshot.DisplayText`에 남아 있던 mojibake 사용량 표시를 정상 한글 문구로 복구했다.
  - [x] 관련 usage/localizer/mojibake focused test 5개와 Tests build로 검증했다.
  - [x] 이후 Desktop build는 WPF/MSBuild 단계에서 timeout되어 완료 evidence가 없으므로 성공으로 기록하지 않는다.

- [x] plan/checkpoint 단순 모델 및 worker-plan adapter 경계를 이어서 직접 읽었다.
  - [x] `AgentCheckpoint`, `AgentCheckpointMessage`, `AgentCheckpointPlanItem`, `AgentCheckpointRunStep`, `AgentPlanItem`, `AgentPlanItemStatus`, `AgentPlanWorkerPlanAdapter`, `AgentQSystemSkill`, `AgentRole`, `AgentRunState`, `AgentRunStep`, `AgentVerificationPlan`을 직접 읽었다.
  - [x] `AgentPlanWorkerPlanAdapter`가 plan item 텍스트에서 unsafe shell command의 앞부분만 잘라 안전한 `RunCommand` step처럼 만들 수 있던 경로를 차단했다.
  - [x] plan adapter는 `VerificationCommandPolicy`가 허용한 명령만 verification/run command로 변환한다.
  - [x] 관련 AgentPlanWorkerPlanAdapter focused test 5개, Tests build, Desktop build로 검증했다.

- [x] attachment/model routing/provider/config 보조 모델 경계를 이어서 직접 읽었다.
  - [x] `DesktopAttachment`, `DesktopExecutionStrategy`, `DesktopGitPromptResult`, `DesktopGitSnapshot`, `DesktopModelRoutingRecommendation`, `DesktopModelRoutingTier`, `DesktopProviderFailureDescription`, `DesktopProviderModelCatalog`, `DesktopProviderModelDiscoveryService`, `DesktopProjectConfigBuilder`, `DesktopToolCapabilitySnapshot`, `DesktopVerificationWorkflowResult`를 직접 읽었다.
  - [x] `DesktopProjectConfigBuilder`가 unsafe verification command를 `.agentq/config.json` 후보로 저장할 수 있던 경로를 차단했다.
  - [x] project config builder도 `VerificationCommandPolicy`가 허용한 명령만 저장한다.
  - [x] 관련 ProjectConfigBuilder/AgentPlanWorkerPlanAdapter focused test 7개, Tests build, Desktop build로 검증했다.

- [x] tool callback / file-change / verification artifact 표시 경계를 이어서 직접 읽었다.
  - [x] `DesktopToolCallbacks`, `DesktopToolCallbacksFactory`, `FileChangeRecord`, `FileChangeReviewStatus`, `FileMutationSnapshot`, `VerificationArtifact`를 직접 읽었다.
  - [x] max tool step 연장 확인창에 남아 있던 mojibake 문구를 정상 한글로 복구했다.
  - [x] 관련 mojibake/usage/config/adapter focused test 10개, Tests build, Desktop build로 검증했다.

- [x] Git/MCP/replay/memory/embedding/worker scaffold 단순 모델 경계를 이어서 직접 읽었다.
  - [x] `GitBranchRecoveryAnalyzer`, `GitBranchStatusAnalyzer`, `GitChangeReviewStatus`, `GitCommandResult`, `GitPullSafetyAnalysis`, `GitPullSafetyAnalyzer`, `McpServerConfig`, `McpToolInfo`, `McpToolName`, `ToolReplayEntry`, `ToolReplaySession`, `ProjectAgentConfig`, `ProjectMemory`, `ProjectMemoryGc`, `EmbeddingIndexBuildResult`, `EmbeddingIndexChunk`, `EmbeddingIndexManifest`, `LinkFetchResult`, `WorkerExecutionContext`, `WorkerPlan`, `WorkerPlanApprovalSummary`, `WorkerPlanPreview`, `WorkerPlanValidation`, `WorkerScaffoldContext`를 직접 읽었다.
  - [x] `WorkerScaffoldContextBuilder`가 symlinked source root/package manifest를 scaffold hint로 사용할 수 있던 경로를 차단했다.
  - [x] 관련 worker scaffold context focused test 3개, Tests build, Desktop build로 검증했다.

- [x] screenshot/source/workspace/verification/multi-agent DTO 경계를 이어서 직접 읽었다.
  - [x] `ScreenshotEvidenceQuality`, `ScreenshotLlmVisionReviewModels`, `ScreenshotVisualReviewCandidate`, `ScreenshotVisualReviewResult`, `SourceFileEntry`, `CodeSymbol`, `WorkspaceDependencyEdge`, `WorkspaceDependencyGraph`, `WorkspaceAnalysis`, `WorkspaceSymbolIndex`, `VerificationRunResult`, `VerificationResultCard`, `VerificationFailureAnalysis`, `VerificationFailureKind`, `PermissionRiskLevel`, `ToolPermissionAssessment`, `ToolPermissionDecision`, `ToolPermissionPolicyResult`, `MultiAgentRolePlan`, `NativeWorkerModels`, `PythonWorkerModels`, `TypeScriptWorkerModels`, `DesktopTaskKind`, `DesktopTaskProfile`을 직접 읽었다.
  - [x] 해당 묶음은 DTO/표시 모델이며 직접 실행을 유발하지 않고, 기존 indexing/worker/verification service 경계에서 path/policy 검증을 수행한다.

- [x] TODO 누락 Services 파일 묶음을 이어서 직접 읽었다.
  - [x] `AgentWorkMode`, `DesktopProjectScaffoldPlanTool`, `DesktopProjectScaffoldVerifyTool`, `DesktopUiConstants`, `IDesktopLlmProviderFactory`, `IVerificationArtifactCollector`, `LineDiffBuilder`, `ModelReasoningTagFilter`, `ProjectScaffoldPlanRegistry`, `RecoveryStrategyRouter`, `VideoFrameExtractor`, `WorkerScaffoldExecution`을 직접 읽었다.
  - [x] scaffold verify tool은 planId/planHash/workspace snapshot/approved command/policy를 확인하고, 실패 검증은 `succeeded=false` JSON으로 반환함을 재확인했다.
  - [x] 이 묶음에서 새 코드 수정이 필요한 실행/완료 오인 경로는 발견하지 않았다.
  - [x] `docs/TODO.md` 기준으로 `csharp/AgentQ.Desktop/Services` 파일명이 모두 감사 기록에 포함되는지 재계산했고, 누락 목록이 비어 있음을 확인했다.

- [x] root project instruction 문서 오염 경계를 확인했다.
  - [x] `AGENTS.md`가 mojibake 상태로 남아 있어 Agent Q 프로젝트 지침이 prompt context를 오염시킬 수 있던 문서 경로를 정상 UTF-8 한국어/영문 원문으로 복구했다.

### L. 핵심 guardrail coverage 대조

- [x] 최신 사용자 요청 우선권 / embedded evidence 분리
  - [x] `UserTurnUnderstanding_*`, `DesktopAgentService_ContextStartsWithLatestUserRequestPriority`, `DesktopAgentService_RoutedUserMessageSeparatesCurrentRequestFromEmbeddedEvidence`, 원래 사고 e2e가 붙여넣은 예시/quoted/log command를 현재 요청과 분리한다.
- [x] Conversation / Action / Hybrid / Ambiguous 실행 분리
  - [x] `TurnIntentClassifier_*`, `DesktopTaskClassifier_*`, `UserIntentTranslator_*`, `DesktopAgentService_BlocksWriteToolForConversationBeforePermissionRequest`, `DesktopAgentService_BlocksReadOnlyShellForConversationBeforePermissionRequest`가 대화형 turn의 write/shell 실행을 permission prompt 전에 막는다.
- [x] no-tool 완료 환각 / 실제 도구 evidence 요구
  - [x] `TaskContractCompletionChecker_*`, `DesktopAgentService_DirectFallbackUsesTaskContractForNoToolCreateDirectoryAnswer`, `CliNonInteractiveRunner_FailsNoToolCompletionClaimForActionPrompt`, `CliNonInteractiveRunner_FailsKoreanNoToolCompletionClaimForActionPrompt`가 실행 evidence 없는 완료 주장을 거부한다.
- [x] deterministic scaffold / worker / local server 경계
  - [x] `ProjectScaffoldPlanner_*`, `DesktopProjectScaffoldCreateTool_*`, `WorkerScaffoldExecutor_*`, `WorkerExecutionPipeline_*`, `DesktopPlanCommandService_*`, `DesktopLocalServerService_*`, `DesktopAgentService_RunLocalServerContractReportsFailedRunStepWhenStartupFails`가 plan 승인, path 검증, 실패 상태, local server evidence를 검증한다.
- [x] verification truthfulness / final answer consistency
  - [x] `ShellVerificationResultDetector_*`, `DesktopAgentService_ReplacesSuccessFinalWhenVerificationEvidenceFailed`, `DesktopVerificationCommandService_*`, `DesktopVerificationPanelWorkflowService_*`, `DesktopAutoFixWorkflowService_*`, `VerificationResultCard_*`가 failed/cancelled verification을 성공 evidence로 쓰지 않게 한다.
- [x] session summary / checkpoint / memory 오염 방지
  - [x] `DesktopSessionSummaryBuilder_*`, `DesktopCheckpointWorkflowService_*`, `DesktopPromptBuilder_BuildResume*`, `ProjectMemoryService_*`, `ExecutionLessonMemoryService_*`, `DesktopLearningSuggestionService_*`, `ChatConversationHistory_CompactWithSummaryDoesNotSplitToolUseAndResultPair`가 historical evidence와 최신 요청을 분리한다.
- [x] provider tool-call protocol / tool id 정규화
  - [x] `ToolCallDeltaBuffer_*`, `ChatContent_ToolFactoriesNormalizeProviderIdentifiers`, `OpenAi*Tool*`, `Anthropic*Tool*`, `GenerateResponseAsync_ParsesLegacyFunctionCall`, `GenerateStreamAsync_AssemblesLegacyFunctionCall`가 provider tool metadata와 protocol pair를 검증한다.
- [x] workspace boundary / permission / tool safety
  - [x] `WorkspacePathResolver`, `DirectorySymlink`, `ToolPermissionClassifier_*`, `ToolPermissionPolicy_*`, `ReadFileTool_*`, `WriteFileTool_*`, `EditFileTool_*`, `CreateDirectoryTool_*`, `DeletePathTool_*`, `GrepTool_*`, `GlobTool_*`, `ToolPathGuard`가 외부 path, symlink, binary, destructive/network/install 권한 경계를 검증한다.
- [x] CLI parity
  - [x] `ExecuteConversationTurnAsync_*`, `CliNonInteractiveRunner_*`, `NonInteractivePermissionEnforcer_*`, `ConsolePermissionEnforcer_*`, `CliInteractiveToolCommands_*`가 CLI에서도 malformed input, permission denial, max-step, no-tool 완료 환각을 막는다.

### M. 최종 완료 조건

- [x] `csharp/AgentQ.Desktop/Services` 전체 파일을 직접 읽었다.
- [x] `csharp/AgentQ.Desktop` UI/event 흐름을 직접 읽었다.
- [x] `csharp/AgentQ.Core` 전체 provider abstraction/message/tool-call core를 직접 읽었다.
- [x] `csharp/AgentQ.Providers.OpenAi` 전체를 직접 읽었다.
- [x] `csharp/AgentQ.Providers.Anthropic` 전체를 직접 읽었다.
- [x] `csharp/AgentQ.Tools` 전체 tool 구현을 직접 읽었다.
- [x] `csharp/AgentQ.Cli` 전체 CLI 실행/permission/tool loop를 직접 읽었다.
- [x] `csharp/AgentQ.Tests`에서 핵심 guardrail coverage가 실제 요구사항을 커버하는지 확인했다.
- [x] 원래 사고, 즉 “구체적인 생성 요청 + 붙여넣은 엉뚱한 답변 예시”가 end-to-end로 안전하게 처리되는지 검증했다.
  - [x] 현재 요청이 `test2 폴더를 생성해줘`이고 뒤에 엉뚱한 답변 예시가 붙은 경우, 예시에 오염되지 않고 `create_directory` direct fallback으로 실제 `test2` 폴더를 생성하는 e2e를 추가했다.
  - [x] 반대로 “Agent Q가 엉뚱한 답변을 한다”는 meta feedback이나 quoted/log command는 permission prompt 전에 Conversation으로 차단되는 e2e를 함께 재확인했다.
- [x] 주요 회귀 테스트 묶음을 통과시켰다.
- [x] `dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false`가 통과했다.
- [x] 남은 위험과 의도적으로 미룬 UX 개선을 문서에 분리해 기록했다.

## 최근 실행한 검증 명령

```powershell
dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. 한국어 UI 문구 복원 후 재확인.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~DesktopUsageSnapshot_DisplayText|FullyQualifiedName~DesktopServicesSource_DoesNotContainKnownMojibakeUiText" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 3, 실패 0, 건너뜀 0, 전체 3. 한국어 UI 문구 복원 후 재확인.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 1050, 실패 0, 건너뜀 0, 전체 1050, 기간 2 m 55 s. 한국어 UI 문구 복원 후 재확인.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. 한국어 UI 문구 복원 후 재확인.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. Playwright artifact/link fetch/TaskExecutor verification guard 수정 후 재확인.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~TaskExecutor_|FullyQualifiedName~Decomposer_|FullyQualifiedName~TaskContextSelector_|FullyQualifiedName~LinkContentFetcher_|FullyQualifiedName~PlaywrightVerificationArtifactCollector_" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 26, 실패 0, 건너뜀 0, 전체 26.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. Playwright artifact/link fetch/TaskExecutor verification guard 수정 후 재확인.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. context/index/scaffold metadata boundary 수정 후 재확인.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~DesktopScaffoldIntentRouter_|FullyQualifiedName~CSharpRoslynAnalysisService_|FullyQualifiedName~EmbeddingIndexBuilder_|FullyQualifiedName~DesktopSourceBrowserService_" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 18, 실패 0, 건너뜀 0, 전체 18.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. context/index/scaffold metadata boundary 수정 후 재확인.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. Git/MCP metadata boundary 수정 후 재확인.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~McpServerRegistry_|FullyQualifiedName~McpBridgeTool_|FullyQualifiedName~McpToolName_|FullyQualifiedName~DesktopGitService_ExcludesAgentMetadataFromChangedFiles" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 9, 실패 0, 건너뜀 0, 전체 9.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. Git/MCP metadata boundary 수정 후 재확인.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. search/symbol/dependency metadata boundary 수정 후 재확인.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~WorkspaceSymbolIndexService_|FullyQualifiedName~WorkspaceDependencyGraphService_|FullyQualifiedName~DesktopSemanticSearchTool_|FullyQualifiedName~DesktopHybridSearchTool_" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 14, 실패 0, 건너뜀 0, 전체 14.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. search/symbol/dependency metadata boundary 수정 후 재확인.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. worker validator resolved path guard 수정 후 재확인.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~WorkerPlanValidator_|FullyQualifiedName~WorkerPlanPreviewBuilder_|FullyQualifiedName~WorkerExecutionPipeline_|FullyQualifiedName~WorkerScaffoldExecutor_|FullyQualifiedName~DesktopVerificationWorkflowService_|FullyQualifiedName~DesktopVerificationCommandService_|FullyQualifiedName~DesktopAgentRunWorkflowService_" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 55, 실패 0, 건너뜀 0, 전체 55.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. worker validator resolved path guard 수정 후 재확인.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. screenshot visual evidence resolved path guard 수정 후 재확인.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~ScreenshotEvidenceQualityChecker_|FullyQualifiedName~ScreenshotVisualReviewService_|FullyQualifiedName~ScreenshotVisualHeuristicEvaluator_|FullyQualifiedName~ScreenshotLlmVisionReviewer_|FullyQualifiedName~VerificationArtifactEvidenceBuilder_" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 10, 실패 0, 건너뜀 0, 전체 10.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. screenshot visual evidence resolved path guard 수정 후 재확인.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. telemetry redaction 수정 후 재확인.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~DesktopTelemetryService_|FullyQualifiedName~SensitiveTextRedactor_|FullyQualifiedName~DesktopDiagnosticsService_|FullyQualifiedName~DesktopFileChangeReviewService_|FullyQualifiedName~FileMutationSnapshotService_|FullyQualifiedName~VisualEvidenceService_" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 11, 실패 0, 건너뜀 0, 전체 11.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 통과, 경고 0, 오류 0. telemetry redaction 수정 후 재확인.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~DesktopProjectScaffoldCreateTool_SkipsExistingFilesAndCreatesMissingFilesByDefault|FullyQualifiedName~DesktopAgentService_PreflightsBareNewProjectClarification|FullyQualifiedName~ProjectMemoryService_BuildContext_SkipsLowConfidenceAndStaleLessons|FullyQualifiedName~DesktopAgentService_ContextForEmptyGreenfieldProjectBlocksWorkflowAnalysis|FullyQualifiedName~DesktopProjectScaffoldCreateTool_CompletesPartialViteScaffoldWhenTopLevelFilesExist|FullyQualifiedName~DesktopAgentService_WritesDiagnosticsForSafeScaffoldLifecycle|FullyQualifiedName~DesktopAgentService_BuildsCollisionSummaryForExistingProjectScaffoldFiles|FullyQualifiedName~DesktopAgentService_AttachesRelevantSystemSkillContext|FullyQualifiedName~DesktopAgentService_AllowsPermissionDeniedReportForDeleteAction|FullyQualifiedName~UserIntentTranslator_RecognizesConcreteKoreanPortfolioProjectRequest" --logger "console;verbosity=minimal"

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~EditFileTool_PreservesUtf16EncodingWhenEditingExistingFile|FullyQualifiedName~WriteFileTool_PreservesUtf16EncodingWhenOverwritingExistingTextFile|FullyQualifiedName~EditFileTool_RejectsBinaryFiles|FullyQualifiedName~WriteFileTool_RejectsBinaryOverwrite|FullyQualifiedName~ReadFileTool_RejectsBinaryFiles" --logger "console;verbosity=minimal"

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Tools\AgentQ.Tools.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~WriteFileTool_|FullyQualifiedName~EditFileTool_" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Tools\AgentQ.Tools.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~ListDirectoryTool_" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Tools\AgentQ.Tools.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~GrepTool_|FullyQualifiedName~GlobTool_" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Tools\AgentQ.Tools.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~WebSearchTool_" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Cli\AgentQ.Cli.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~CliNonInteractiveRunner_|FullyQualifiedName~ExecuteConversationTurnAsync_" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Cli\AgentQ.Cli.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~ConsolePermissionEnforcer_|FullyQualifiedName~CliInteractiveToolCommands_" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~MainViewModel_StatusAccentBrush|FullyQualifiedName~RunSummaryViewModel_DoesNotShowNegatedCompletionAsCompleted|FullyQualifiedName~MainViewModel_TracksLocalServerStateForFooterActions|FullyQualifiedName~MainViewModel_DisablesResumeAndContinueActionsWhileBusy|FullyQualifiedName~DesktopAgentRunWorkflowService_PrepareContinuation_DoesNotOverwriteUserDraft|FullyQualifiedName~ProjectPanelViewModel_ResetEmptyState_ClearsStaleAnalysisReadiness|FullyQualifiedName~ProjectPanelViewModel_ApplyAnalysis" --logger "console;verbosity=minimal"

git ls-files 'docs/hermes-agent-porting.ko.md' 'docs/hermes-import-notes.md' 'docs/superpowers/plans/2025-04-07-pork-damage-flash.md' 'docs/DEVELOPMENT_PLAN.md' 'docs/TODO.md' 'docs/Agent Q.md'

git status --short -- 'docs/hermes-agent-porting.ko.md' 'docs/hermes-import-notes.md' 'docs/superpowers/plans/2025-04-07-pork-damage-flash.md' 'docs/DEVELOPMENT_PLAN.md' 'docs/TODO.md' 'docs/Agent Q.md'

rg -n '筌|癰|濡|沅|諛|獄|野|揶|嚥|袁|뚯씪|꾧뎄|묒뾽|쒕쾭|덈뀞|섏꽭' csharp docs -g '*.cs' -g '*.md'

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_ContextForUnrealFeasibilityQuestionAvoidsSessionAndLinkDrift|FullyQualifiedName~DesktopAgentService_ContextStartsWithLatestUserRequestPriority|FullyQualifiedName~DesktopAgentService_BuildContextOmitsExecutionLessonsForConversationOnlyTurn|FullyQualifiedName~DesktopAgentService_BuildContextReportsLinkFetchFailureWithoutCategoricalNoAccess|FullyQualifiedName~DesktopAgentService_ContextIncludesRunLocalServerTaskContract|FullyQualifiedName~DesktopAgentService_BuildContextDoesNotTouchExecutionLessonMemory" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_DirectFallbackReportsFailedRunStepWhenPermissionDenied|FullyQualifiedName~DesktopAgentService_DirectFallbackUsesRawUserTextWhenRoutingTextIsPathOnly|FullyQualifiedName~DesktopAgentService_DirectFallbackDeletesFolderWhenModelAnswersIrrelevantText|FullyQualifiedName~DesktopAgentService_DirectFallbackUsesTaskContractForNoToolCreateDirectoryAnswer|FullyQualifiedName~DesktopAgentService_RecordsTaskContractEvidenceForCreateDirectory" /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_RunLocalServerContractReportsFailedRunStepWhenStartupFails|FullyQualifiedName~DesktopLocalServerService_FailedStartedProcessKeepsAttemptedCommand|FullyQualifiedName~DesktopLocalServerService_PermissionDeniedDoesNotReportExecutedCommand" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_Replaces|FullyQualifiedName~DesktopAgentService_KeepsSuccessFinalWhenChangedFileIsMentioned|FullyQualifiedName~DesktopAgentService_DoesNotReplaceFinalThatAlreadyReportsVerificationFailure|FullyQualifiedName~DesktopAgentService_SafeScaffoldModeReportsFailedRunStepWhenScaffoldDenied|FullyQualifiedName~DesktopAgentService_RunLocalServerContractReportsFailedRunStepWhenStartupFails" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_SafeScaffoldModeReportsFailedRunStepWhenScaffoldDenied|FullyQualifiedName~DesktopAgentService_RunLocalServerContractReportsFailedRunStepWhenStartupFails|FullyQualifiedName~DesktopAgentService_ReplacesSuccessFinalWhenVerificationEvidenceFailed|FullyQualifiedName~DesktopAgentService_DoesNotReplaceFinalThatAlreadyReportsVerificationFailure|FullyQualifiedName~DesktopAgentRunWorkflowService_ClassifiesLocalServerFailureAsFailed|FullyQualifiedName~DesktopAgentRunWorkflowService_ClassifiesScaffoldCreationFailureAsFailed|FullyQualifiedName~DesktopAgentRunWorkflowService_ClassifiesScaffoldNotCreatedAsIncomplete" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_Replaces|FullyQualifiedName~DesktopAgentService_DoesNotReplaceFinalThatAlreadyReportsVerificationFailure|FullyQualifiedName~DesktopAgentService_StepLimitSummaryKeepsFileChangeEvidence|FullyQualifiedName~DesktopAgentRunWorkflowService_Classifies" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_E2eCreatesDirectoryFromExplicitCommand|FullyQualifiedName~TaskContractCompletionChecker_|FullyQualifiedName~DesktopAgentService_NormalizesBlankAndDuplicateToolUseIds|FullyQualifiedName~DesktopAgentService_DirectFallbackUsesTaskContractForNoToolCreateDirectoryAnswer" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_NormalizesBlankAndDuplicateToolUseIds|FullyQualifiedName~DesktopAgentService_AllowsSafeReadToolWhenIntentIsConversation|FullyQualifiedName~DesktopAgentService_BlocksWriteToolForConversationBeforePermissionRequest|FullyQualifiedName~DesktopAgentService_BlocksReadOnlyShellForConversationBeforePermissionRequest|FullyQualifiedName~DesktopAgentService_DoesNotTrackBlockedShellCommandAsExecuted" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopVerificationCommandService_|FullyQualifiedName~DesktopVerificationPanelWorkflowService_|FullyQualifiedName~DesktopAutoFixWorkflowService_|FullyQualifiedName~VerificationResultCard_|FullyQualifiedName~DesktopVerificationWorkflowService_RedactsSecretsFromSummary" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopVerificationCommandService_|FullyQualifiedName~DesktopVerificationPanelWorkflowService_|FullyQualifiedName~DesktopAutoFixWorkflowService_" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopPlanCommandService_" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~WorkerScaffoldExecutor_" /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopPlanCommandService_ExecutesWorkerScaffold|FullyQualifiedName~DesktopPlanCommandService_SkipsWorkerScaffold|FullyQualifiedName~DesktopPlanCommandService_BlocksWorkerScaffold" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~WorkerExecutionPipeline_" /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~WorkerExecutionPipeline_|FullyQualifiedName~WorkerScaffoldExecutor_" /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopPlanCommandService_ExecutesWorkerScaffold|FullyQualifiedName~DesktopPlanCommandService_PreparesWorkerRepair|FullyQualifiedName~DesktopPlanCommandService_RunsWorkerRepair|FullyQualifiedName~DesktopPlanCommandService_SkipsWorkerScaffold|FullyQualifiedName~DesktopPlanCommandService_BlocksWorkerScaffold" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopSessionSummaryBuilder_|FullyQualifiedName~DesktopCheckpointWorkflowService_|FullyQualifiedName~DesktopAgentService_ContextForUnrealFeasibilityQuestionAvoidsSessionAndLinkDrift|FullyQualifiedName~DesktopAgentService_BuildRequestMessagesOmitsOffTargetHistoricalAssistantText|FullyQualifiedName~UserTurnUnderstanding_|FullyQualifiedName~DesktopWorkspaceContextWorkflowService_AutoSessionSummaryDoesNotOverwriteRunStatus" /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_RecordsCreateDirectoryAsFileChange|FullyQualifiedName~DesktopAgentService_RecordsEmptyFileDeletionAsFileChange|FullyQualifiedName~DirectorySymlink|FullyQualifiedName~WorkspacePathResolver|FullyQualifiedName~ToolPermissionClassifier_|FullyQualifiedName~FileMutationSnapshotService" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~TurnIntentClassifier_" /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore --filter "FullyQualifiedName~UserTurnUnderstanding_" /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore --filter "FullyQualifiedName~UserIntentTranslator_|FullyQualifiedName~TaskContract_" /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore --filter "FullyQualifiedName~DesktopTaskClassifier_" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore --filter "FullyQualifiedName~ProjectMemoryService_|FullyQualifiedName~ExecutionLessonMemoryService_|FullyQualifiedName~DesktopLearningSuggestionService_" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore --filter "FullyQualifiedName~DesktopToolInputParser_|FullyQualifiedName~DesktopAgentService_BlocksMalformedJsonToolInputBeforePermission|FullyQualifiedName~DesktopAgentService_DoesNotTrackBlockedShellCommandAsExecuted|FullyQualifiedName~ExecuteConversationTurnAsync_BlocksMalformedToolInputBeforePermission" /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false

dotnet build csharp\AgentQ.Providers.OpenAi\AgentQ.Providers.OpenAi.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~GenerateResponseAsync_NormalizesInvalidHistoricalToolMessages|FullyQualifiedName~GenerateResponseAsync_SendsExpectedOpenAiCompatibleRequest|FullyQualifiedName~OpenAiResponse_HandlesMissingUsage_AndToolCalls|FullyQualifiedName~AnthropicRequest_SendsStringifiedToolInputAsObject" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Providers.Anthropic\AgentQ.Providers.Anthropic.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~GenerateResponseAsync_NormalizesInvalidHistoricalToolMessages|FullyQualifiedName~GenerateResponseAsync_SendsExpectedOpenAiCompatibleRequest|FullyQualifiedName~OpenAiResponse_HandlesMissingUsage_AndToolCalls|FullyQualifiedName~AnthropicRequest_SendsStringifiedToolInputAsObject|FullyQualifiedName~AnthropicRequest_DropsBlankToolResultIds|FullyQualifiedName~AnthropicResponse_DropsWhitespaceToolMetadata" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Providers.Anthropic\AgentQ.Providers.Anthropic.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

# 테스트 어셈블리 컴파일이 한 차례 timeout 된 뒤 MSBuild/VBCSCompiler 서버 정리.
dotnet build-server shutdown

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~GenerateResponseAsync_NormalizesInvalidHistoricalToolMessages|FullyQualifiedName~GenerateResponseAsync_SendsExpectedOpenAiCompatibleRequest|FullyQualifiedName~OpenAiResponse_HandlesMissingUsage_AndToolCalls|FullyQualifiedName~AnthropicRequest_SendsStringifiedToolInputAsObject|FullyQualifiedName~AnthropicRequest_DropsBlankToolResultIds|FullyQualifiedName~AnthropicResponse_DropsWhitespaceToolMetadata|FullyQualifiedName~AnthropicStream_EmitsMergedUsageAtMessageStop|FullyQualifiedName~AnthropicResponse_IncludesCacheInputTokensInUsage" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Providers.OpenAi\AgentQ.Providers.OpenAi.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~OpenAiStream_EmitsReasoningTextAndToolUse|FullyQualifiedName~GenerateResponseAsync_ParsesToolCallsAndUsage|FullyQualifiedName~GenerateResponseAsync_ParsesLegacyFunctionCall|FullyQualifiedName~GenerateResponseAsync_UsesLegacyFunctionCallWhenToolCallsAreInvalid|FullyQualifiedName~GenerateStreamAsync_AssemblesMultipleToolCalls|FullyQualifiedName~GenerateStreamAsync_AssemblesLegacyFunctionCall|FullyQualifiedName~GenerateResponseAsync_NormalizesInvalidHistoricalToolMessages" --logger "console;verbosity=minimal"

dotnet build-server shutdown

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~Compact_ShouldNotSplitRecentToolUseAndToolResultProtocolPair|FullyQualifiedName~Compact_ShouldSummarizeToolUsesAndResults|FullyQualifiedName~Compact_ShouldKeepLatestUserRequestWhenHistoryIsLarge|FullyQualifiedName~GenerateResponseAsync_NormalizesInvalidHistoricalToolMessages" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Core\AgentQ.Core.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~ProviderConfiguration_FromArgs_|FullyQualifiedName~ConfigStore_SaveAndLoad_RoundTripsProviderConfiguration|FullyQualifiedName~ConfigStore_SaveAsync_ReplacesExistingFileWithoutLeavingTempFiles" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Tools\AgentQ.Tools.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~BashTool_|FullyQualifiedName~ExecuteConversationTurnAsync_IncludesDefaultSystemPrompt|FullyQualifiedName~DesktopPromptAssemblyService_AddsTaskSpecificGuidance|FullyQualifiedName~CliNonInteractiveRunner_TreatsBashNonZeroExitAsFailure" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Tools\AgentQ.Tools.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~ReadFileTool_" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Core\AgentQ.Core.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:BuildProjectReferences=false /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~ChatContent_ToolFactoriesNormalizeProviderIdentifiers|FullyQualifiedName~ToolCallDeltaBuffer_|FullyQualifiedName~ProviderConfiguration_FromArgs_|FullyQualifiedName~ConfigStore_SaveAndLoad_RoundTripsProviderConfiguration|FullyQualifiedName~ConfigStore_SaveAsync_ReplacesExistingFileWithoutLeavingTempFiles|FullyQualifiedName~GenerateResponseAsync_NormalizesInvalidHistoricalToolMessages" --logger "console;verbosity=minimal"

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~DesktopLocalServerService_ReusesAndStopsWorkspaceSession|FullyQualifiedName~DesktopLocalServerService_ReadShortErrorIgnoresLockedLogFiles|FullyQualifiedName~DesktopLocalServerService_FailedStartedProcessKeepsAttemptedCommand|FullyQualifiedName~DesktopLocalServerService_PermissionDeniedDoesNotReportExecutedCommand" --logger "console;verbosity=minimal"

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 1040, 실패 0, 건너뜀 0, 전체 1040, 기간 3 m 8 s.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~WorkerScaffoldExecutor_" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 21, 실패 0, 건너뜀 0, 전체 21.

dotnet build csharp\AgentQ.Tools\AgentQ.Tools.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~CreateDirectoryTool_|FullyQualifiedName~DeletePathTool_|FullyQualifiedName~DirectorySymlink|FullyQualifiedName~ToolPathGuard" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 14, 실패 0, 건너뜀 0, 전체 14.

rg -n "筌|癰|濡|沅|諛|獄|野|揶|嚥|袁|뚯씪|꾧뎄|묒뾽|쒕쾭|덈뀞|섏꽭|�" csharp\AgentQ.Tools -g "*.cs"
# 2026-06-12 결과: match 없음(exit 1).

dotnet build csharp\AgentQ.Cli\AgentQ.Cli.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~CliNonInteractiveRunner_|FullyQualifiedName~CliConfigurationLoader_|FullyQualifiedName~NonInteractivePermissionEnforcer_" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 10, 실패 0, 건너뜀 0, 전체 10.

dotnet build csharp\AgentQ.Cli\AgentQ.Cli.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~ChatConversationHistory_CompactWithSummaryDoesNotSplitToolUseAndResultPair|FullyQualifiedName~CliNonInteractiveRunner_|FullyQualifiedName~ExecuteConversationTurnAsync_" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 24, 실패 0, 건너뜀 0, 전체 24.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 1046, 실패 0, 건너뜀 0, 전체 1046, 기간 2 m 25 s.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~DesktopAgentService_CreatesExplicitFolderWhenPastedIrrelevantAnswerFollowsRequest|FullyQualifiedName~DesktopAgentService_DoesNotExecuteEmbeddedFolderCommandInBadResponseComplaint|FullyQualifiedName~DesktopAgentService_BlocksToolCallForEmbeddedCommandEvenWhenLlmIntentSaysAction|FullyQualifiedName~DesktopAgentService_DoesNotExecuteQuotedFolderCommandInLogAnalysis" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 4, 실패 0, 건너뜀 0, 전체 4.

rg -n "筌|癰|濡|沅|諛|獄|野|揶|嚥|袁|뚯씪|꾧뎄|묒뾽|쒕쾭|덈뀞|섏꽭|�|沅|怨|留|援|蹂|遺" csharp\AgentQ.Cli csharp\AgentQ.Tests\AutomationSupportTests.cs csharp\AgentQ.Tests\CliToolLoopRunnerTests.cs -g "*.cs"
# 2026-06-12 결과: match 없음(exit 1).

# 2026-06-12 확인: full dotnet test 재시도 중 DesktopLocalServerService_ReusesAndStopsWorkspaceSession이
# child process가 아직 잡고 있는 stderr log를 File.ReadAllTextAsync로 읽다가 IOException으로 실패했다.
# `ReadShortErrorAsync`를 shared read + IOException/UnauthorizedAccessException empty fallback으로 고치고
# DesktopLocalServerService_ReadShortErrorIgnoresLockedLogFiles 회귀 테스트와 focused local-server 4개 테스트로 검증했다.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopAgentService_DoesNotTrackFailedProjectScaffoldVerificationAsExecutedCommand|FullyQualifiedName~DesktopAgentService_TracksSuccessfulProjectScaffoldVerificationAsExecutedCommand|FullyQualifiedName~DesktopVerificationSelector_" -v minimal
# 2026-06-12 결과: 통과 9, 실패 0, 건너뜀 0, 전체 9. NuGet vulnerability metadata 조회 경고(NU1900)는 있었지만 테스트는 통과했다.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 0, 오류 0.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopConfidenceAssessor_|FullyQualifiedName~DesktopAgentService_DoesNotTrackFailedProjectScaffoldVerificationAsExecutedCommand|FullyQualifiedName~DesktopAgentService_TracksSuccessfulProjectScaffoldVerificationAsExecutedCommand|FullyQualifiedName~DesktopVerificationSelector_" -v minimal
# 2026-06-12 결과: 통과 16, 실패 0, 건너뜀 0, 전체 16. NuGet vulnerability metadata 조회 경고(NU1900)는 있었지만 테스트는 통과했다.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 5개(NU1900 package vulnerability metadata 조회 실패), 오류 0.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --filter "FullyQualifiedName~DesktopServicesSource_DoesNotContainKnownMojibakeUiText|FullyQualifiedName~DesktopPermissionEnforcer_|FullyQualifiedName~ToolPermission" -v minimal
# 2026-06-12 결과: 병렬 Desktop build와 동시에 실행되어 AgentQ.Desktop.dll 파일 잠금(CS2012)으로 실패했다. 코드/테스트 실패 evidence로 보지 않고 단독 재실행했다.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 5개(NU1900 package vulnerability metadata 조회 실패), 오류 0.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~DesktopServicesSource_DoesNotContainKnownMojibakeUiText|FullyQualifiedName~DesktopPermissionEnforcer_|FullyQualifiedName~ToolPermission" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 66, 실패 0, 건너뜀 0, 전체 66.

rg -n "媛|留|異|嫄|誘|鍮|寃|臾|野|꾩|묒|쒕|놁|�|筌|癰|濡|沅|諛|獄|揶|嚥|袁" csharp/AgentQ.Desktop/Services -g "*.cs"
# 2026-06-12 결과: match 없음(exit 1).

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~DesktopServicesSource_DoesNotContainKnownMojibakeUiText|FullyQualifiedName~DesktopPermissionEnforcer_|FullyQualifiedName~ToolPermission" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 66, 실패 0, 건너뜀 0, 전체 66.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 5개(NU1900 package vulnerability metadata 조회 실패), 오류 0.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 7개(NU1900 package vulnerability metadata 조회 실패), 오류 0.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: Tests build와 병렬 실행되어 WPF MarkupCompile.cache 파일 잠금(MSB4018/IOException)으로 실패했다. 코드 실패 evidence로 보지 않고 MSBuild 서버 정리 후 단독 재실행했다.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~DesktopVerificationRunner_DoesNotDeleteVerificationOutputSymlinkTarget|FullyQualifiedName~DesktopServicesSource_DoesNotContainKnownMojibakeUiText" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 2, 실패 0, 건너뜀 0, 전체 2.

dotnet build-server shutdown
# 2026-06-12 결과: MSBuild/VB/C# 컴파일러 서버 종료 완료.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 4개(NU1900 package vulnerability metadata 조회 실패), 오류 0.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 7개(NU1900 package vulnerability metadata 조회 실패), 오류 0.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~WorkspaceIndexer_IgnoresAgentMetadataAndEmptyCommandArtifactsForEmptyWorkspace|FullyQualifiedName~DesktopVerificationRunner_DoesNotDeleteVerificationOutputSymlinkTarget" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 2, 실패 0, 건너뜀 0, 전체 2.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 4개(NU1900 package vulnerability metadata 조회 실패), 오류 0.

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 7개(NU1900 package vulnerability metadata 조회 실패), 오류 0.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --filter "FullyQualifiedName~WorkspaceAnalysisService_IgnoresAgentQMetadataForEmptyWorkspace|FullyQualifiedName~WorkspaceIndexer_IgnoresAgentMetadataAndEmptyCommandArtifactsForEmptyWorkspace|FullyQualifiedName~DesktopVerificationRunner_DoesNotDeleteVerificationOutputSymlinkTarget" --logger "console;verbosity=minimal"
# 2026-06-12 결과: 통과 3, 실패 0, 건너뜀 0, 전체 3.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 4개(NU1900 package vulnerability metadata 조회 실패), 오류 0.

# 2026-06-11 시도: WPF generated .g.cs stale 상태를 정리한 뒤에도 5분 제한에서 timeout.
# 의존 프로젝트 빌드 출력은 있었지만 AgentQ.Desktop 본체 완료 evidence는 없으므로 성공으로 취급하지 않는다.
dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal

dotnet build csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 결과: 빌드 성공, 경고 7개(NU1900 package vulnerability metadata 조회 실패), 오류 0. 의존 빌드로 AgentQ.Desktop.dll도 생성됨.

dotnet test csharp\AgentQ.Tests\AgentQ.Tests.csproj --no-build --logger "console;verbosity=minimal"
# 2026-06-12 첫 시도 결과: 640개 통과 뒤 테스트 호스트 종료 단계 중단으로 exit code 1. 성공 evidence로 취급하지 않고 단독 재시도함.
# 2026-06-12 재시도 결과: 통과 1092, 실패 0, 건너뜀 0, 전체 1092, 기간 1 m 36 s.

dotnet build csharp\AgentQ.Desktop\AgentQ.Desktop.csproj --no-restore /p:UseSharedCompilation=false /p:NodeReuse=false /m:1 --verbosity:minimal
# 2026-06-12 재시도 결과: 빌드 성공, 경고 4개(NU1900 package vulnerability metadata 조회 실패), 오류 0.
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
