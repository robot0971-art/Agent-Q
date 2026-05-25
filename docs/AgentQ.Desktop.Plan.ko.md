# AgentQ Desktop 계획 문서

## 목표

AgentQ Desktop은 기존 AgentQ CLI의 provider, tool, permission 흐름을 재사용하는 Windows WPF 코딩 에이전트 앱이다. 사용자는 프로젝트 폴더를 선택하고, 채팅으로 작업을 요청하며, 도구 실행과 파일 변경을 UI에서 확인하고 승인할 수 있다.

## 현재 범위

- WPF 기반 Windows 데스크톱 앱
- `AgentQ.Core`, `AgentQ.Tools`, `AgentQ.Providers.*` 재사용
- OpenCode Go, OpenAI 호환 provider, Anthropic provider 설정
- provider, model, base URL, API key, timeout, max tokens 설정
- 프로젝트 폴더 선택 및 workspace context 자동 첨부
- 채팅 입력, 스트리밍 응답, 이미지/동영상 첨부
- 도구 실행 권한 확인 및 승인 흐름
- 파일 변경 기록, Git 상태/diff 조회
- 계획 생성, 체크포인트 저장/불러오기, 세션 요약
- 빌드/테스트 검증 명령 추천 및 실행 결과 카드
- Settings, Chat, Project, Git, Verification, Plan, Memory, File Change Review, Run Timeline 패널의 UserControl 분리
- Auto Fix, 첨부 파일 선택, 클립보드 처리를 별도 서비스로 분리
- provider별 모델 목록을 `DesktopProviderModelCatalog`로 분리
- Git 브랜치 상태 안내: upstream 없음, upstream 삭제, ahead/behind/diverged 상태 표시
- Git stage/commit 기본 흐름: 선택 파일 stage, 승인 파일 stage, 선택 파일 unstage, staged commit
- Git pull 안전 흐름: 깨끗한 작업트리와 안전한 브랜치 상태에서만 `git pull --ff-only` 허용
- Git 브랜치 복구 기본 흐름: 현재 HEAD 백업 브랜치 생성, 깨끗한 작업트리에서만 `main` 전환
- global tool 업데이트 smoke test 검증: pack, tool update, installed/direct CLI 실행 성공

## 권장 작업 순서

1. `MainWindow.xaml.cs`의 조정 로직을 focused workflow/service로 계속 옮긴다.
2. Desktop 서비스 단위 테스트를 계속 보강한다.
3. Docker 기반 CLI/MockService/test 환경을 검토한다.
4. 데모 시나리오를 따라 실제 UX와 안정성을 반복 확인한다.
5. UI 문구, 빈 상태, 오류 메시지, 패널 스캔성을 정리한다.

## 주요 사용 흐름

1. 사용자가 AgentQ Desktop을 실행한다.
2. provider, model, base URL, API key를 설정한다.
3. 프로젝트 폴더를 선택한다.
4. `Analyze`로 프로젝트 맵과 검증 명령을 갱신한다.
5. 채팅으로 작업을 요청한다.
6. AgentQ가 필요한 파일을 읽고, 검색하고, 수정하거나 명령을 실행한다.
7. 위험한 도구 실행은 UI에서 승인하거나 거절한다.
8. 파일 변경, Git diff, 검증 결과, 다음 작업을 확인한다.
9. 필요하면 체크포인트나 세션 요약으로 나중에 작업을 이어간다.
10. 변경 내용을 리뷰하고 검증한 뒤 Git 패널에서 commit을 준비한다.

## 기술 선택

- UI: WPF
- Runtime: .NET 10
- Language: C#
- 설정 저장: `ConfigStore`와 호환되는 JSON 설정
- LLM 호출: 기존 provider 계층
- 도구 실행: 기존 `ToolRegistry`와 tool 구현
- DI: `Microsoft.Extensions.DependencyInjection`

## 안정성 기준

- `.\build.ps1`이 로컬 환경에서 통과한다.
- `.\test.ps1`이 로컬 환경에서 통과한다.
- Desktop 프로젝트가 경고 없이 빌드된다.
- 권한 확인, 계획 파서, Git 상태 파서, workspace 분석, 검증 실패 분류가 단위 테스트로 보호된다.
- 사용자에게 보이는 문서와 UI 문자열이 UTF-8로 정상 표시된다.
- UserControl과 workflow/service 분리가 기존 사용 흐름을 깨지 않는다.

## 구조 개선 방향

현재 Desktop 기능은 `MainWindow.xaml`과 `MainWindow.xaml.cs`에서 점진적으로 분리 중이다. Settings, Chat, Project, Git, Verification, Plan, Memory, File Change Review, Run Timeline 패널은 이미 UserControl로 분리되었다.

다음 단계에서는 브랜치 복구 UX와 제품 polish를 보강하고, `MainWindow.xaml.cs`의 조정 로직을 focused workflow/service로 더 옮겨 변경 범위를 줄인다.

분리 완료 또는 진행 중인 영역:

- Settings panel
- Chat panel
- Project panel
- Git panel
- Verification panel
- Plan panel
- Memory panel
- File change review panel
- Run timeline panel
- Eval dashboard panel
- Checkpoint/session summary workflow
- Auto Fix workflow

## 보안 기준

- workspace root 밖의 읽기와 쓰기를 차단한다.
- destructive 명령은 Desktop 권한 정책에서 차단한다.
- `git push`, tag 생성 같은 원격/릴리즈 작업은 기본 차단한다.
- 쓰기 작업은 자동 허용할 수 있지만, shell 실행은 작업 모드에 따라 승인 흐름을 거친다.
- 파일 변경은 snapshot과 diff preview를 통해 검토할 수 있어야 한다.

## 제품화 체크리스트

- `docs/demo-scenarios.md`의 세 가지 데모가 반복 가능해야 한다.
- Run summary가 다음 액션과 검증/커밋 준비 상태를 명확히 보여야 한다.
- Project dashboard가 프로젝트 맵, key files, symbols, dependencies, verification command 상태를 요약해야 한다.
- Change preview에서 Auto Fix 후 승인/수정 필요/되돌리기 흐름이 분명해야 한다.
- Plan 패널에서 선택된 항목과 Evidence/Verification/Eval 상태가 연결되어 보여야 한다.
- README와 설치 안내가 현재 기능과 일치해야 한다.
