# AgentQ Desktop 계획 문서

## 목표

AgentQ Desktop은 기존 AgentQ CLI의 provider, tool, permission 흐름을 재사용하는 Windows WPF 코딩 에이전트입니다. 사용자는 프로젝트 폴더를 선택하고, 채팅으로 작업을 지시하며, 도구 실행과 파일 변경을 UI에서 확인할 수 있어야 합니다.

## 현재 범위

- WPF 기반 Windows 데스크톱 앱
- `AgentQ.Core`, `AgentQ.Tools`, `AgentQ.Providers.*` 재사용
- OpenCode Go, OpenAI 호환 provider, Anthropic provider 설정
- provider, model, base URL, API key, timeout, max tokens 설정
- 프로젝트 폴더 선택 및 workspace context 자동 첨부
- 채팅 입력, 스트리밍 응답, 이미지/동영상 첨부
- 도구 실행 권한 정책과 승인 흐름
- 파일 변경 기록, Git 상태/diff 조회
- 계획 생성, 체크포인트 저장/불러오기, 세션 요약
- 빌드/테스트 검증 명령 추천 및 실행 결과 카드
- Git 패널과 검증 패널의 UserControl 분리
- Auto Fix, 첨부 파일 선택, 클립보드 처리를 별도 서비스로 분리

## 권장 작업 순서

1. 현재 Desktop 분리 작업 diff 검수
2. Desktop 서비스 단위 테스트 보강
3. README와 Desktop 사용 문서 최신화
4. Git stage/commit/pull UX 강화
5. provider별 모델 목록을 설정 파일 또는 동적 갱신 구조로 이동
6. UI 패널을 추가로 분리해 `MainWindow.xaml`과 code-behind 부담 축소

## 주요 사용자 흐름

1. 사용자가 AgentQ Desktop을 실행한다.
2. provider, model, base URL, API key를 설정한다.
3. 프로젝트 폴더를 선택한다.
4. 채팅으로 작업을 요청한다.
5. 에이전트가 필요한 파일을 읽고, 검색하고, 수정하거나 명령을 실행한다.
6. 위험한 도구 실행은 UI에서 승인 또는 거절한다.
7. 파일 변경, Git diff, 검증 결과, 다음 작업을 확인한다.
8. 필요하면 체크포인트나 세션 요약으로 나중에 이어서 작업한다.

## 기술 선택

- UI: WPF
- Runtime: .NET 10
- Language: C#
- 설정 저장: `ConfigStore`와 호환되는 JSON 설정
- LLM 호출: 기존 provider 계층
- 도구 실행: 기존 `ToolRegistry`와 tool 구현
- DI: `Microsoft.Extensions.DependencyInjection`

## 안정화 기준

- `dotnet test csharp/AgentQ.sln`이 로컬 환경에서 통과한다.
- Desktop 프로젝트가 경고 없이 빌드된다.
- 권한 정책, 계획 파서, Git 상태 파서, workspace 분석, 검증 실패 분류가 단위 테스트로 보호된다.
- 사용자에게 보이는 문서와 UI 문자열이 UTF-8로 정상 표시된다.
- 새 UserControl과 workflow/service 분리가 기존 사용자 흐름을 깨지 않는다.

## 구조 개선 방향

현재 Desktop 기능은 `MainWindow.xaml`과 `MainWindow.xaml.cs`에서 점진적으로 분리 중입니다. 다음 단계에서는 설정, 채팅, 계획, 체크포인트/세션 요약 패널도 UserControl 또는 workflow/service로 분리해 변경 범위를 줄입니다.

분리 후보:

- Settings panel
- Chat panel
- Plan panel
- Checkpoint/session summary panel
- File change review panel

## 보안 기준

- workspace root 밖의 읽기와 쓰기를 차단한다.
- destructive 명령은 Desktop 정책에서 차단한다.
- `git push`, tag 생성 같은 원격/릴리스 작업은 기본 차단한다.
- 읽기 작업은 자동 허용할 수 있지만, 쓰기와 shell 실행은 작업 모드에 따라 승인 흐름을 거친다.
