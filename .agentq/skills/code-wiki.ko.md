---
id: hermes-code-wiki
title: Hermes Code Wiki (Korean Port)
priority: 55
taskKinds: documentation,analysis,general
triggers: wiki,docs,architecture,문서,위키,구조,아키텍처,정리
excludes: 구현만,수정만
---
# 코드 위키 작성

코드베이스 구조를 사용자가 다시 찾아보기 쉽게 정리하는 스킬입니다.

## 절차

1. 솔루션/프로젝트/주요 폴더 구조를 먼저 파악합니다.
2. 핵심 런타임 흐름을 찾습니다. 예: Desktop UI -> workflow service -> agent service -> tools/provider.
3. 변경 가능성이 낮은 사실만 문서화합니다.
4. 파일 경로와 클래스 이름을 정확히 적습니다.
5. TODO나 추측은 "추정" 또는 "후보"로 분리합니다.

## AgentQ 문서 기준

- 기능별로 Desktop, Core, Tools, Tests 경계를 나눕니다.
- 새 프로젝트 생성, tool execution, permission, memory, local server처럼 사용자 흐름 중심으로 설명합니다.
- 외부 프로젝트에서 가져온 아이디어는 그대로 복사하지 말고 AgentQ 용어로 번역합니다.
- 한글 문서는 UTF-8로 저장합니다.

## 산출물 예

- `docs/*architecture*.md`
- 기능별 "현재 구조 / 문제 / 개선 방향"
- 작업 전후 비교표
