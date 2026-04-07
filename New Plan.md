# AgentQ New Plan

Updated: 2026-04-07

## 현재 상태 평가

AgentQ는 초기 프로토타입 단계를 지난 상태다.
현재 기준으로는 핵심 아키텍처, CLI 대화 루프, 멀티 Provider 구조, 기본 Tool 세트, Mock parity 테스트까지 이미 갖춰져 있다.

진행률 관점에서는 대략 `70~85%` 수준으로 판단한다.

- 기술 데모 기준: 거의 완료
- 내부 개발용 베타 기준: 완료에 가까움
- 외부 배포 가능한 v1 기준: 아직 마감 작업이 남아 있음

즉, 지금은 "만드는 단계"보다 "다듬고 검증하는 단계"에 들어선 프로젝트로 보는 것이 맞다.

---

## 이미 끝난 영역

다음 영역은 핵심 구현이 완료되었거나, 최소한 실사용 가능한 수준까지 올라와 있다.

- 솔루션 구조 분리 완료
  - `AgentQ.Api`
  - `AgentQ.Core`
  - `AgentQ.Providers.Anthropic`
  - `AgentQ.Providers.OpenAi`
  - `AgentQ.Tools`
  - `AgentQ.Cli`
  - `AgentQ.MockService`
  - `AgentQ.Tests`

- CLI REPL과 기본 명령 체계 구현 완료
  - `/help`
  - `/clear`
  - `/run`
  - `/provider`
  - `/model`
  - `/base-url`
  - `/timeout`
  - `/config save`
  - `/save`
  - `/load`

- Agent loop 구현 완료
  - 사용자 입력
  - LLM 호출
  - Tool call 수집
  - Tool 실행
  - Tool result 반영
  - 다음 대화 턴 연결

- 멀티 Provider 구조 도입 완료
  - `ILlmProvider`
  - `ProviderFactory`
  - `ResilientLlmProvider`
  - Anthropic Provider
  - OpenAI 호환 Provider

- 핵심 Tool 세트 구현 완료
  - `bash`
  - `read_file`
  - `write_file`
  - `edit_file`
  - `grep`
  - `glob`

- 기본 보안 제약 적용 완료
  - `ToolPathGuard`
  - workspace root 바깥 접근 제한
  - permission 기반 도구 실행 흐름

- 테스트 기반 검증 체계 확보
  - Tool/config 테스트
  - OpenAI 호환 Provider 테스트
  - Mock parity integration 테스트

---

## 아직 부족한 영역

현재 부족한 부분은 구조 자체보다 제품 완성도와 운영 품질에 가깝다.

- CLI UX는 usable하지만 아직 polished하지 않음
  - 상태 표시와 오류 메시지 정리 필요
  - 긴 응답 및 긴 tool 출력 가독성 개선 필요
  - 설정 변경 후 반영 경험 정리 필요

- Provider abstraction은 좋지만 coverage가 넓지 않음
  - 현재 실질 구현은 Anthropic, OpenAI 호환 중심
  - Gemini, Ollama, 기타 Provider 확장은 아직 후순위

- 테스트는 의미 있게 존재하지만 v1 수준 회귀 방어선은 더 필요함
  - CLI end-to-end 시나리오 보강 필요
  - permission denial 흐름 검증 보강 필요
  - 장문 스트리밍/실패 복구 시나리오 보강 필요
  - 운영 환경 차이 검증 보강 필요

- Tool 안전성 정책은 더 강화할 여지가 있음
  - 특히 `bash`는 timeout, 출력 길이, 허용 범위, 오류 처리 정책을 더 엄격히 볼 필요가 있음

- 문서와 실제 구현 상태 사이에 시차가 있음
  - 초기 설계 문서보다 구현이 앞서 있음
  - 현재 상태 기준으로 문서 동기화 필요

---

## v1 출시 전 핵심 우선순위

다음 5개를 최우선 작업으로 본다.

### 1. CLI End-to-End 회귀 테스트 강화

가장 먼저 해야 할 일이다.
현재 테스트는 방향이 좋지만, 실제 사용자 흐름 전체를 막는 수준까지는 아직 부족하다.

강화 대상:

- 질문 -> 스트리밍 -> tool call -> permission -> tool result -> 최종 응답
- multi-tool turn
- tool failure 후 복구
- permission denied 후 후속 응답
- session save/load 이후 대화 지속

목표:

- CLI 핵심 동작 변경 시 회귀를 즉시 잡을 수 있는 상태 만들기

### 2. Tool 안전성 정책 보강

v1에서 가장 위험한 부분은 기능 부족보다 tool 오남용이다.
특히 `bash`는 정책이 느슨하면 전체 제품 신뢰도를 무너뜨릴 수 있다.

강화 대상:

- 실행 timeout
- 허용 범위 제한
- 출력 크기 제한
- 민감 경로 차단
- 에러 메시지 정리
- permission 요청 UX 개선

목표:

- "강력한 도구"와 "예측 가능한 안전성"을 함께 확보하기

### 3. CLI UX 마감

현재 CLI는 개발자 베타 느낌이 강하다.
작동 여부가 아니라 사용 경험 기준으로 다듬는 작업이 필요하다.

개선 대상:

- slash command 안내 정리
- 실패 시 다음 행동 안내
- 긴 응답 렌더링
- 긴 tool 출력 요약 방식
- provider/model/base-url 변경 경험 개선

목표:

- 신규 사용자도 큰 혼선 없이 사용할 수 있는 수준 만들기

### 4. Provider 안정화 전략 확정

지금 시점에서는 Provider 수를 늘리는 것보다 현재 지원 범위를 안정화하는 것이 우선이다.

우선 방침:

- Anthropic 안정화
- OpenAI 호환 안정화
- 공통 DTO/stream/tool-call 처리 일관성 강화

후순위:

- Gemini
- Ollama
- 기타 OpenAI-compatible vendor 확장

목표:

- 확장성보다 신뢰 가능한 기본 Provider 품질 먼저 확보하기

### 5. 문서와 구현 상태 동기화

현재 문서는 일부가 과거 시점 기준이다.
새 작업자나 미래의 자신을 위해 지금 상태를 기준으로 다시 맞춰야 한다.

정리 대상:

- `Agent.md`
- `README.md`
- `New Plan.md`

반영할 내용:

- 현재 솔루션 구조
- `net10.0`
- 실제 지원 Provider
- 실제 slash command 목록
- 설정 저장 방식
- 테스트 범위와 현재 수준

목표:

- 문서를 읽으면 현재 코드 상태를 정확히 이해할 수 있게 만들기

---

## 우선순위 요약

실행 우선순위는 다음 순서를 따른다.

1. 테스트
2. 안전성
3. UX
4. Provider 전략
5. 문서 정리

---

## 실행 메모

실제로 작업을 시작한다면 다음 순서가 가장 효율적이다.

1. `AgentQ.Tests`에서 CLI end-to-end 시나리오를 먼저 보강한다.
2. `AgentQ.Tools`에서 `bash` 중심 안전성 정책을 강화한다.
3. `AgentQ.Cli`에서 사용자 경험과 에러 안내를 정리한다.
4. Provider별 안정화 범위를 확정하고 테스트를 맞춘다.
5. 마지막에 문서를 현재 코드 기준으로 전체 동기화한다.

---

## 결론

AgentQ는 핵심 기능을 새로 만드는 시점은 거의 지났다.
이제부터의 성패는 기능 추가보다도 다음에 달려 있다.

- 회귀를 막는 테스트
- 안전한 Tool 실행
- 신뢰 가능한 Provider 동작
- 예측 가능한 CLI UX
- 현재 상태를 반영하는 문서

이 다섯 가지를 마감하면 v1에 가까운 상태로 올라갈 수 있다.
