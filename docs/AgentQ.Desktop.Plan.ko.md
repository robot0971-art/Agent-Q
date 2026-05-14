# AgentQ Desktop 기획 문서

## 목표

AgentQ Desktop은 기존 AgentQ CLI의 코어 기능을 재사용하면서, Windows에서 아이콘을 눌러 실행할 수 있는 데스크톱 코딩 에이전트 앱이다.

터미널 사용에 익숙하지 않은 사용자도 프로젝트 폴더를 선택하고, 채팅으로 작업을 지시하고, 도구 실행과 파일 변경을 눈으로 확인할 수 있게 만드는 것이 1차 목표다.

## 핵심 사용자 경험

1. 사용자가 `AgentQ Desktop`을 실행한다.
2. 첫 실행 화면에서 provider, model, base URL, API key를 설정한다.
3. 프로젝트 폴더를 선택한다.
4. 채팅창에 작업을 요청한다.
5. 에이전트가 필요한 도구 실행을 요청하면 사용자가 승인하거나 거부한다.
6. 파일 변경이 필요한 경우 diff를 보여준다.
7. 테스트나 빌드 결과를 작업 로그에서 확인한다.
8. 완료 후 변경 요약과 다음 추천 작업을 보여준다.

## 1차 범위

첫 버전은 "작동하는 데스크톱 채팅 에이전트"에 집중한다.

- WPF 기반 Windows 데스크톱 앱
- 기존 `AgentQ.Core`, `AgentQ.Tools`, `AgentQ.Providers.*` 재사용
- OpenCode Go 설정 지원
- provider/model/base URL/API key 설정 화면
- 채팅 입력 및 응답 출력
- 프로젝트 폴더 선택
- 읽기 도구 중심의 안전한 작업 실행
- 도구 실행 승인 다이얼로그
- 설정 저장 및 불러오기

## 제외할 항목

첫 버전에서는 아래 기능을 뒤로 미룬다.

- 백그라운드 트레이 상주
- 자동 예약 작업
- 복잡한 멀티 탭 작업공간
- GitHub PR 생성
- 실시간 diff 편집기
- 플러그인 마켓플레이스
- 다중 세션 동시 실행

## 기술 선택

### 권장 기술

- UI: WPF
- 런타임: .NET 10
- 언어: C#
- 설정 저장: 기존 `ConfigStore`와 호환되는 JSON 설정
- LLM 호출: 기존 provider 계층 재사용
- 도구 실행: 기존 `ToolRegistry` 및 tool 구현 재사용

### WPF를 선택하는 이유

- 현재 코드베이스가 C#/.NET 기반이라 재사용이 쉽다.
- Windows 전용 앱 목표에 잘 맞는다.
- WinUI 3보다 초기 설정과 배포가 단순하다.
- Electron보다 가볍고, 기존 C# 도메인 로직과 붙이기 쉽다.

## 프로젝트 구조 초안

```text
csharp/
|- AgentQ.Desktop
|  |- App.xaml
|  |- MainWindow.xaml
|  |- ViewModels
|  |  |- MainViewModel.cs
|  |  |- SettingsViewModel.cs
|  |  `- ToolApprovalViewModel.cs
|  |- Services
|  |  |- DesktopAgentService.cs
|  |  |- DesktopConfigService.cs
|  |  `- WpfPermissionEnforcer.cs
|  `- Views
|     |- SettingsView.xaml
|     |- ChatView.xaml
|     `- ToolApprovalDialog.xaml
|- AgentQ.Cli
|- AgentQ.Core
|- AgentQ.Tools
|- AgentQ.Providers.Anthropic
`- AgentQ.Providers.OpenAi
```

## 화면 구성

### 메인 화면

메인 화면은 작업에 바로 들어갈 수 있어야 한다.

- 왼쪽: 프로젝트 및 세션 영역
- 가운데: 채팅 영역
- 오른쪽: 작업 로그 및 도구 실행 내역
- 하단: 입력창, 전송 버튼, 중지 버튼

### 설정 화면

설정 화면은 첫 실행과 이후 수정에 모두 사용한다.

- Provider 선택
- Model 입력
- Base URL 입력
- API key 입력
- Timeout 설정
- Max tokens 설정
- 설정 저장 버튼
- 연결 테스트 버튼

### 도구 승인 다이얼로그

도구 실행 전 사용자가 무엇을 허용하는지 명확히 보여준다.

- 도구 이름
- 설명
- 실행 인자
- 위험도 표시
- 이번만 허용
- 이번 세션 동안 허용
- 거부

## 내부 흐름

```text
사용자 입력
-> MainViewModel
-> DesktopAgentService
-> CliToolLoopRunner 또는 공통 Agent Runner
-> Provider
-> StreamingProcessor
-> ToolExecutor
-> WpfPermissionEnforcer
-> UI에 결과 반영
```

## 구현 단계

### 1단계: WPF 프로젝트 추가

- `AgentQ.Desktop` 프로젝트 생성
- 솔루션에 추가
- 기존 프로젝트 참조 연결
- 빈 MainWindow 실행 확인

완료 기준:

- `dotnet run --project .\csharp\AgentQ.Desktop`로 창이 열린다.

### 2단계: 설정 화면

- provider/model/base URL/API key 입력 UI 생성
- OpenCode Go 기본값 제공
- 기존 config 파일과 호환되게 저장

완료 기준:

- 앱에서 API key를 저장하고 다시 실행해도 설정이 유지된다.

### 3단계: 채팅 기본 연결

- 채팅 입력창과 메시지 목록 구현
- 기존 provider로 일반 응답 받기
- 스트리밍 텍스트를 UI에 표시

완료 기준:

- 사용자가 "안녕"을 입력하면 모델 응답이 UI에 표시된다.

### 4단계: 프로젝트 폴더 선택

- 폴더 선택 버튼 추가
- 선택한 폴더를 `AGENTQ_WORKSPACE_ROOT` 또는 별도 실행 컨텍스트에 반영
- 현재 작업 폴더 표시

완료 기준:

- 선택한 프로젝트 안의 파일을 읽는 요청이 정상 동작한다.

### 5단계: 도구 승인 UI

- `WpfPermissionEnforcer` 구현
- 도구 실행 요청을 다이얼로그로 표시
- 승인/거부 결과를 `ToolExecutor`에 반환

완료 기준:

- `read_file`, `bash`, `write_file` 같은 도구 요청이 UI 승인 흐름을 탄다.

### 6단계: 작업 로그

- provider 호출 시작/완료
- 도구 실행 시작/완료
- 오류
- 권한 거부
- 최종 요약

완료 기준:

- 사용자가 에이전트가 지금 무엇을 하고 있는지 UI에서 따라갈 수 있다.

### 7단계: 배포

- Windows self-contained publish
- `AgentQ.Desktop.exe` 생성
- `publish-desktop-win.cmd` 추가
- 이후 installer는 별도 단계에서 검토

완료 기준:

- .NET SDK가 없는 Windows에서도 실행 가능한 폴더 배포물을 만들 수 있다.

## 보안 기준

Desktop 버전도 CLI와 같은 보안 기준을 유지한다.

- workspace root 밖 파일 접근 차단
- 심볼릭 링크/정션 경로 우회 차단
- 위험 shell 명령 차단
- 쓰기/편집/실행 도구는 기본적으로 사용자 승인 필요
- API key는 화면에 마스킹 표시
- 로그에 API key 원문 기록 금지

## 한글 및 인코딩 기준

한글 깨짐을 막기 위해 아래 기준을 지킨다.

- 모든 문서와 소스 파일은 UTF-8로 저장한다.
- Markdown 문서는 `.md` 확장자를 사용한다.
- Windows CMD 출력이 필요한 스크립트는 `chcp 65001`을 고려한다.
- 앱 내부 문자열은 C# 소스에 UTF-8로 저장한다.
- JSON 설정 파일은 UTF-8로 저장한다.
- 콘솔 출력용 문자열과 WPF UI 문자열을 분리할 수 있게 준비한다.

## 1차 MVP 완료 기준

- Windows에서 `AgentQ.Desktop` 창이 열린다.
- 설정 화면에서 OpenCode Go API key를 저장할 수 있다.
- 채팅으로 모델 응답을 받을 수 있다.
- 프로젝트 폴더를 선택할 수 있다.
- 파일 읽기 도구를 승인하고 결과를 받을 수 있다.
- 작업 로그가 표시된다.
- CLI 기능에는 회귀가 없다.

## 다음 의사결정

1. WPF 기본 UI를 먼저 만들지, MVVM 구조를 먼저 잡을지 결정한다.
2. 기존 `CliToolLoopRunner`를 Desktop에서도 그대로 쓸지, 공통 `AgentRunner`로 이름과 위치를 바꿀지 결정한다.
3. 설정 저장을 CLI와 완전히 공유할지, Desktop 전용 설정을 둘지 결정한다.

권장 결정:

- 첫 단계에서는 WPF 기본 UI를 먼저 만든다.
- 기존 `CliToolLoopRunner`를 그대로 재사용한다.
- 설정 파일은 CLI와 공유한다.
