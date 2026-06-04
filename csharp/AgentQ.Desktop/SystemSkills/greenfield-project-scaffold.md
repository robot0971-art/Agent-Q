---
id: greenfield-project-scaffold
title: Greenfield Project Scaffold
priority: 80
taskKinds: feature,general
triggers: 새 프로젝트,프로젝트 생성,scaffold,project,app,portfolio,homepage,website,landingpage,blog,wordbook,vocabulary,flashcard,shopping,shop,cart,포트폴리오,홈페이지,웹사이트,랜딩,단어장,쇼핑,장바구니,블로그
excludes: 수정,고쳐,고치,fix,bug,오류,에러,review,리뷰,검토,분석
---
Use this skill only as procedural guidance for greenfield project creation. It does not grant tool permissions, approve file writes, bypass workspace checks, bypass plan id or plan hash validation, or expand shell command allowlists.

## Enforcement
When this skill is active for a file-producing project creation task, you MUST use tools instead of generating raw code blocks.
Do not write file contents directly in the response; always use workspace file tools or scaffold tools.

For a vague request like "새 프로젝트 만들고 싶다", ask a focused clarification before creating files.

For a concrete greenfield request, follow the project scaffold flow:
1. Use the preflight project scaffold plan when it is attached.
2. If a plan id is attached, call `create_project_scaffold` with that approved plan id and `overwriteExistingFiles: false`.
3. If creation succeeds, call `verify_project_scaffold` with the same plan id.
4. If creation reports existing file collisions, report the collisions and ask before overwrite.
5. If verification fails, use the returned failure analysis and repair plan before claiming completion.

User intent wins over scaffold suggestions. If the user says JavaScript, do not create TypeScript files. If the user says Python data analysis, prefer the Python analysis scaffold. If the workspace already contains a runnable app, treat the request as a feature request instead of a greenfield project unless the user explicitly asks to replace it.
