---
id: hermes-test-driven-development
title: Hermes Test Driven Development (Korean Port)
priority: 70
taskKinds: feature,bug-fix,refactor,verification-failure
triggers: test,tdd,red green,regression,coverage,테스트,TDD,회귀,검증,커버리지
excludes: 설명만,분석만,리뷰만
---
# 테스트 주도 개발

Hermes의 RED-GREEN-REFACTOR 흐름을 AgentQ 작업 방식에 맞춘 스킬입니다.

## 절차

1. 사용자가 원하는 동작을 한 문장으로 정리합니다.
2. 그 동작을 가장 작게 실패시키는 테스트를 추가하거나 기존 테스트를 선택합니다.
3. 테스트가 실패하는 이유가 의도한 이유인지 확인합니다.
4. 최소 구현으로 테스트를 통과시킵니다.
5. 통과 후 중복, 이름, 경계, 오류 처리를 정리합니다.
6. 관련 테스트와 빌드를 다시 실행합니다.

## AgentQ 적용

- C# 서비스/도구 변경은 `AgentQ.Tests`에 좁은 unit test를 먼저 추가합니다.
- WPF UI 변경은 ViewModel 단위 테스트가 가능하면 우선합니다.
- scaffold, permission, file mutation처럼 안전성이 중요한 기능은 "성공 케이스"와 "막아야 하는 케이스"를 함께 둡니다.
- 테스트가 너무 무거우면 핵심 순수 함수나 helper를 분리해 작은 테스트를 둡니다.

## 피해야 할 것

- 테스트 없이 큰 동작을 한 번에 구현하지 않습니다.
- 실패 이유를 보지 않고 바로 구현하지 않습니다.
- 통과 후 unrelated refactor를 섞지 않습니다.
