---
id: hermes-systematic-debugging
title: Hermes Systematic Debugging (Korean Port)
priority: 75
taskKinds: bug-fix,verification-failure,analysis,general
triggers: bug,fix,error,failed,failure,debug,crash,regression,broken,timeout,exception,버그,수정,고장,오류,에러,실패,디버그,크래시,회귀,안됨,문제,예외
excludes: review,리뷰,검토만,분석만
---
# 체계적 디버깅

Hermes의 root-cause debugging 절차를 AgentQ Desktop 흐름에 맞춘 스킬입니다.

## 원칙

증상만 고치는 수정은 실패로 보기 쉽습니다. 수정 전에는 원인을 좁히고, 증거로 확인한 뒤, 가장 작은 변경을 적용합니다.

## 절차

1. 오류 메시지, stack trace, 실패 로그를 끝까지 읽습니다.
2. 가능한 가장 좁은 명령으로 재현합니다. 예: 단일 테스트, 단일 빌드 대상, 실패 프로젝트만.
3. 최근 변경과 관련 파일을 확인합니다. `grep_search`, `symbol_search`, `hybrid_search`, `read_file`을 우선 사용하고, 필요할 때만 `bash`로 Git/build/test 명령을 실행합니다.
4. 데이터가 어디서 깨지는지 경계별로 확인합니다. UI, service, provider, file tool, process runner, config loader처럼 AgentQ의 계층을 나누어 봅니다.
5. 한 번에 하나의 가설만 테스트합니다. "X가 원인이라면 Y가 보여야 한다" 형태로 구체화한 뒤 작은 변경으로 검증합니다.
6. 검증이 실패하면 같은 수정을 반복하지 말고 가설을 바꿉니다.

## AgentQ 우선순위

- C# 데스크톱 문제: 관련 ViewModel, Service, XAML, 테스트 순서로 조사합니다.
- provider/tool 문제: `AgentQ.Core`, provider 프로젝트, `AgentQ.Tools`, CLI/desktop callback 경계를 분리합니다.
- 빌드 실패: `dotnet build`의 첫 번째 실제 compiler error를 기준으로 봅니다.
- 테스트 실패: 실패한 테스트 이름과 assertion을 먼저 읽고, 테스트가 표현하는 사용자 동작을 확인합니다.

## 완료 조건

- 재현 명령 또는 관찰 근거가 있습니다.
- 원인 파일/계층을 좁혔습니다.
- 변경이 원인에 직접 연결됩니다.
- 가능한 경우 좁은 검증을 다시 실행했습니다.
