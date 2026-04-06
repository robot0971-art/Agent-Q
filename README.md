# AgentQ (에이전트 큐) 🚀

**AgentQ**는 멀티 LLM(Anthropic, OpenAI compatible)을 지원하며, 강력한 도구 사용(Tool-use) 능력을 갖춘 터미널 기반의 AI 코딩 어시스턴트입니다. 

기존의 단순한 챗봇을 넘어, 직접 파일을 읽고 쓰고, 쉘 명령어를 실행하며 프로젝트 전체를 이해하고 코딩 작업을 수행할 수 있도록 설계되었습니다.

---

## ✨ 핵심 기능 (Key Features)

- **멀티 LLM 지원**: Anthropic Claude 3.5, OpenAI GPT-4o 등 다양한 모델을 지원하며 자유롭게 전환 가능합니다.
- **강력한 도구 세트**:
  - `bash`: 시스템 명령어 실행
  - `read_file` / `write_file` / `edit_file`: 정밀한 파일 조작
  - `grep` / `glob`: 효율적인 코드 검색
- **안전한 보안 설계**: `AGENTQ_WORKSPACE_ROOT` 설정을 통해 에이전트의 활동 범위를 특정 디렉토리로 제한합니다.
- **세션 및 설정 관리**:
  - `/save`, `/load`: 대화 내역을 파일로 저장하고 나중에 다시 시작할 수 있습니다.
  - `/config save`: 현재 사용 중인 모델과 API 설정을 영구적으로 저장합니다.
- **네트워크 복원력**: 지수 백오프(Exponential Backoff) 기반의 재시도 로직이 탑재되어 네트워크 불안정 시에도 안정적으로 동작합니다.
- **풍부한 터미널 UI**: `Spectre.Console`을 활용한 스피너, 마크다운 렌더링, 구문 강조 기능을 제공합니다.

---

## 🛠 시작하기 (Getting Started)

### 요구 사항
- **.NET 10 SDK** 이상

### 빌드 및 실행
```bash
# 프로젝트 빌드
dotnet build csharp/AgentQ.sln

# CLI 실행
dotnet run --project csharp/AgentQ.Cli
```

### 환경 변수 설정
필요한 설정은 환경 변수나 `config.json`을 통해 관리할 수 있습니다.
- `AGENTQ_PROVIDER`: 사용할 프로바이더 (anthropic, openai 등)
- `AGENTQ_MODEL`: 사용할 모델 이름
- `AGENTQ_API_KEY`: LLM 서비스 API 키
- `AGENTQ_WORKSPACE_ROOT`: 에이전트가 접근 가능한 루트 디렉토리 (기본값: 현재 디렉토리)

---

## ⌨️ 슬래시 명령어 (Slash Commands)

CLI 내에서 다음과 같은 명령어를 사용할 수 있습니다.

| 명령어 | 설명 |
| :--- | :--- |
| `/help` | 사용 가능한 명령어 목록 표시 |
| `/save <path>` | 현재 대화 세션을 JSON 파일로 저장 |
| `/load <path>` | 이전 대화 세션을 불러오기 |
| `/config save` | 현재 설정을 `config.json`에 영구 저장 |
| `/clear` | 현재 대화 기록 초기화 |
| `/exit` | 프로그램 종료 |
| `/run <tool> <args>` | 도구를 수동으로 직접 실행 |

---

## 🏗 프로젝트 구조

- **AgentQ.Core**: LLM 제공자 인터페이스 및 핵심 모델 정의
- **AgentQ.Tools**: 에이전트가 사용하는 각종 도구(Bash, File IO 등) 구현체
- **AgentQ.Cli**: 사용자 상호작용 및 REPL 루프 담당
- **AgentQ.Api**: 통신을 위한 데이터 계약(DTO) 정의
- **AgentQ.MockService**: 테스트를 위한 모의 API 서버

---

## 📝 라이선스
이 프로젝트는 MIT 라이선스를 따릅니다.
