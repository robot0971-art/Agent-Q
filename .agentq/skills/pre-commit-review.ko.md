---
id: hermes-pre-commit-review
title: Hermes Pre-Commit Review (Korean Port)
priority: 65
taskKinds: feature,bug-fix,refactor,documentation,general
triggers: commit,pre-commit,review,검토,리뷰,커밋,푸쉬,마무리
excludes: 질문만,설명만
---
# 커밋 전 리뷰

커밋 전 변경 범위, 실제 동작, 최종 설명이 서로 맞는지 확인하는 스킬입니다.

## 체크리스트

1. `git status --short`로 변경 파일을 확인합니다.
2. `git diff`로 사용자 요청과 관련 없는 변경이 섞였는지 확인합니다.
3. 새 파일, 삭제 파일, mixed hunk가 있는지 확인합니다.
4. 변경된 동작에 맞는 테스트 또는 빌드를 실행합니다.
5. 최종 답변에 실제 변경 파일과 검증 결과가 과장 없이 반영되는지 확인합니다.

## AgentQ 주의점

- 사용자 변경을 되돌리지 않습니다.
- scaffold, permission, file mutation, provider 설정 변경은 특히 테스트를 남깁니다.
- 한 커밋에는 하나의 의미 있는 묶음만 담습니다.
- 한글 문서는 UTF-8로 읽히는지 확인합니다.
- `.agentq`의 replay, telemetry, diagnostics, embeddings 같은 로컬 산출물은 커밋하지 않습니다.

## 완료 조건

- staged 파일이 의도한 범위와 일치합니다.
- `git diff --cached --check`가 통과합니다.
- 실행한 검증과 실패한 검증을 모두 기록합니다.
