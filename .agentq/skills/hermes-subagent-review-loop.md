---
id: hermes-subagent-review-loop
title: Hermes-Inspired Review Loop
priority: 70
taskKinds: feature,general
triggers: plan,implement,parallel,subagent,delegate,review loop,작업 분해,병렬,회의장,리뷰 루프
excludes: 간단 수정,one-line,질문만,설명만
---
# Hermes-Inspired Review Loop

Use this skill for multi-step implementation work that benefits from a disciplined review loop.

## Procedure

1. Read the implementation plan or infer a concise task list from the user's request.
2. Split work into small tasks that touch mostly independent files.
3. For each task, implement the smallest complete change and run focused verification.
4. Review the result against the original task before moving on:
   - Did every requested behavior land?
   - Did file paths, public API, UI labels, and config names match the request?
   - Did the change avoid unrelated refactors?
5. Then review code quality:
   - project conventions
   - clear naming
   - error handling
   - missing tests or verification
   - security and permission risks
6. Fix any important issue before marking the task complete.
7. After all tasks, run an integration check across the touched surface and summarize changed files.

## AgentQ Adaptation

AgentQ may not have an actual `delegate_task` tool in a run. When no delegation tool is available, simulate the same discipline by separating implementer, spec-review, and quality-review passes in the Plan and Evidence flow.

Do not spawn overlapping work on files that are likely to conflict. Prefer serial execution for shared C# services, XAML view models, provider abstractions, and config models.
