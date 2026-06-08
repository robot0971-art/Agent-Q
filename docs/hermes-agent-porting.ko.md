# Hermes Agent 자료 포팅 노트

이 문서는 `C:\Users\admin\Desktop\hermes-agent-main`에서 AgentQ에 바로 도움이 되는 자료를 선별해 한국어/AgentQ 용어로 옮긴 기록입니다.

## 가져온 것

- `skills/software-development/systematic-debugging`: root cause 우선 디버깅 절차를 `.agentq/skills/systematic-debugging.ko.md`로 이식했습니다.
- `skills/software-development/test-driven-development`: RED-GREEN-REFACTOR 절차를 `.agentq/skills/test-driven-development.ko.md`로 이식했습니다.
- `skills/software-development/requesting-code-review`: 커밋 전 검증/리뷰 흐름을 `.agentq/skills/pre-commit-review.ko.md`로 이식했습니다.
- `optional-skills/software-development/code-wiki`: 코드베이스 위키 생성 절차를 `.agentq/skills/code-wiki.ko.md`로 이식했습니다.

## 그대로 가져오지 않은 것

- Python 런타임 코드: Hermes는 Python CLI/TUI 중심이고 AgentQ는 C#/.NET/WPF 구조라 직접 복사는 유지보수 비용이 큽니다.
- `locales/ko.yaml`: Hermes 쪽 한국어 파일은 현재 콘솔에서 깨져 보이며, AgentQ는 C# `DesktopLocalizer` 기반 한국어 UI가 이미 있습니다.
- 대량 optional skills: AgentQ의 현재 시스템 스킬 로더는 `.agentq/skills/*.md` top-level 파일만 읽고, 한 번에 최대 3개를 모델 컨텍스트에 넣습니다. 대량 복사보다 핵심 절차를 작게 포팅하는 편이 실사용에 안전합니다.
- provider plugin YAML 전체: AgentQ는 Anthropic/OpenAI-compatible provider 추상화를 이미 갖고 있습니다. Hermes provider 목록은 향후 model catalog UI나 provider template 작업 때 참고 자료로 쓰는 편이 적합합니다.

## AgentQ에 맞춘 이식 기준

1. 현재 도구 이름에 맞춥니다: `read_file`, `grep_search`, `symbol_search`, `hybrid_search`, `bash`.
2. 한국어 트리거와 영어 트리거를 함께 둡니다.
3. 외부 서비스나 Python 전용 명령은 필수 단계에서 제외합니다.
4. 파일을 생성하는 지침은 AgentQ의 권한/검증 흐름을 우회하지 않도록 절차형 조언으로만 둡니다.

## 다음 이식 후보

- Hermes `plugins/model-providers/*/plugin.yaml`을 읽어 AgentQ provider preset catalog로 변환
- Hermes `optional-mcps/*/manifest.yaml`을 AgentQ `.agentq/config.json` MCP server template로 변환
- Hermes gateway/platform 아이디어를 AgentQ Desktop의 notification 또는 external channel integration 설계 문서로 정리
- Hermes memory/skill hub 개념을 AgentQ Memory panel의 shared skill browser로 확장
