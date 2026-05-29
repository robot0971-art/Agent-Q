# AgentQ Language Worker Architecture

## Goal

AgentQ Desktop and Core should remain C#/.NET, but multi-language code understanding should not be implemented entirely by hand in C#.

AgentQ should act as an orchestrator:

```text
AgentQ Desktop/Core (C#)
-> language worker
-> JSON project map, symbols, dependencies, commands
-> memory, RAG, evidence, planning, and UI
```

For project creation and larger feature work, workers should not directly mutate the workspace. They should first produce a structured plan, then AgentQ should execute the approved plan with normal file tools, verification, and repair safeguards.

## Why Workers

C# is a good fit for the Windows desktop app, provider/API key management, memory storage, evidence UI, Git workflows, release workflows, and .NET/C# analysis.

Deep multi-language analysis should use each ecosystem's tools:

- TypeScript: TypeScript compiler API, `ts-morph`
- Python: `ast`, `libcst`, `ruff`, `pyright`
- C++: clangd, CMake, `compile_commands.json`, tree-sitter
- Go: `go list`, `go/packages`, `gopls`
- Rust: `cargo metadata`, rust-analyzer
- Unity/Unreal: engine-specific project files and conventions

## Worker Contract

Workers should run as local tools or processes and return JSON.

Input:

```json
{
  "workspaceRoot": "C:/repo",
  "language": "typescript",
  "maxFiles": 5000,
  "includeSymbols": true,
  "includeDependencies": true
}
```

## Plan / Execute / Verify Contract

Advanced worker-assisted creation should be split into separate phases:

```text
user request
-> workspace analysis
-> read-only worker plan
-> approved execute
-> focused verification
-> failure classification
-> repair plan
-> repair execute
-> verification retry
```

Plan phase requirements:

- read-only
- describe the target language, framework, files to create, files to modify, verification commands, and risks
- include reasons for every file operation
- include whether a step requires user approval
- avoid claiming a dependency or framework exists unless worker evidence found it

Plan approval UX should not rely on users reading a long step list. The default approval surface should summarize:

- files to create, modify, and delete
- expected change by subsystem
- risk level and reason
- high-risk areas such as auth, database migrations, security, destructive changes, and broad refactors
- verification commands
- rollback or snapshot availability

Raw plan steps should remain available as expandable detail. Mixed-risk plans should support partial approval, such as approving low-risk edits while sending high-risk migration/auth changes back for plan revision.

Execute phase requirements:

- modify only files listed in the approved plan
- stop and request a follow-up plan if an unplanned file must be changed
- run formatter/build/test commands only through the verification policy
- record all file changes and command results as evidence

Repair loop requirements:

- classify verification failures before editing
- generate a small repair plan for one failure class at a time
- rerun the same focused verification after repair
- stop on repeated identical failure signatures, no file changes, or max attempts
- stop on repeated identical Playwright or visual failure signatures instead of making unbounded UI tweaks

Example plan shape:

```json
{
  "goal": "Create a FastAPI payments feature",
  "language": "python",
  "framework": "FastAPI",
  "steps": [
    {
      "kind": "create_file",
      "path": "app/routers/payments.py",
      "reason": "Expose payment endpoints",
      "requiresApproval": false
    },
    {
      "kind": "modify_file",
      "path": "app/main.py",
      "reason": "Register the payments router",
      "requiresApproval": true
    }
  ],
  "verification": ["python -m pytest"],
  "risks": ["Confirm the existing database/session pattern before adding persistence."]
}
```

## Playwright Verification

Playwright should be integrated first as an external project verification command, not as a fully embedded browser runtime.

Phase 1 should detect:

- `playwright.config.*`
- `@playwright/test`
- package scripts such as `test:e2e`, `e2e`, or `playwright`
- existing report, trace, screenshot, and test-results folders

Phase 1 should run project-owned commands such as:

- `npm run test:e2e`
- `npx playwright test`
- equivalent `pnpm`, `yarn`, or `bun` scripts when detected

Playwright results should feed the same Verify and Auto Fix loop:

```text
web build/unit verification
-> Playwright verification
-> screenshot capture
-> visual heuristics
-> optional LLM screenshot review
-> trace/screenshot/console/network evidence
-> failure classification
-> repair plan
-> repair execute
-> rerun Playwright
```

An AgentQ-owned `playwright-worker.mjs` can come later for smoke tests when a project has no Playwright tests yet.

Visual verification should be layered. Start with cheap deterministic checks before using LLM review:

- blank or mostly empty page
- missing expected primary element
- console errors
- network errors
- viewport overflow or horizontal scrolling
- obvious text overlap or clipped controls
- screenshot diff or repeated identical visual failure signature

LLM screenshot review should be optional and evidence-backed. Use it for ambiguous layout, polish, and visual regression cases that DOM assertions cannot catch.

## Memory Lifecycle

Memory should be treated as a managed cache of project lessons, not an append-only source of truth.

Long-term memory should include:

- source and evidence reference
- confidence score
- usefulness or last-used score
- creation and last-used timestamps
- expiration/TTL
- duplicate merge key
- disabled/retired state

Memory retrieval should prefer current workspace evidence over older memory. When memory conflicts with current files, commands, or worker analysis, AgentQ should either ignore the memory or surface it as stale. Periodic memory GC should remove expired, duplicate, low-usefulness, contradictory, or unsafe entries.

## Core / UI Boundary

Worker orchestration, Plan/Execute/Verify/Repair, permission policy, memory retrieval, failure classification, and verification selection should live in shared core services. Desktop should provide the rich review/evidence/visual UX, but CLI should be able to run the same pipeline.

This keeps the Windows WPF desktop valuable without making the core agent workflow depend on WPF.

Output:

```json
{
  "language": "typescript",
  "projectType": "Next.js",
  "frameworks": ["React", "TypeScript"],
  "roles": [
    { "name": "UI layer", "paths": ["app", "components"] },
    { "name": "API layer", "paths": ["app/api"] }
  ],
  "commands": {
    "build": ["npm run build"],
    "test": ["npm test"],
    "lint": ["npm run lint"]
  },
  "symbols": [
    {
      "name": "LoginForm",
      "kind": "component",
      "path": "components/LoginForm.tsx",
      "line": 12
    }
  ],
  "dependencies": [
    {
      "from": "components/LoginForm.tsx",
      "to": "lib/auth.ts",
      "kind": "import"
    }
  ]
}
```

## Initial Language Priority

1. JavaScript/TypeScript: `package.json`, Next.js, React, Vite, Node, scripts, imports, components.
2. Python: `pyproject.toml`, `requirements.txt`, FastAPI, Django, imports, functions, classes.
3. C++: `CMakeLists.txt`, `.vcxproj`, `.sln`, `src`, `include`, tests, Unreal basics.
4. Go: `go.mod`, packages, commands, tests.
5. Rust: `Cargo.toml`, Cargo workspace metadata.
6. Unity: `Assets`, `ProjectSettings`, `Packages`, scripts, scenes, prefabs later.

## Integration Plan

Phase 1: Multi-language Project Map v1

- keep implementation mostly in C#
- detect project types and common files
- extract build/test/lint command candidates
- improve folder role detection for JS/TS, Python, C++, Go, Rust, Unity, and Unreal

Phase 2: Worker Runner

- define a worker invocation service in C#
- run local Node/Python/native workers safely
- parse JSON output into AgentQ project map models
- record worker results in Evidence Trail

Phase 3: Symbol Index v1

- start with TypeScript and Python workers
- keep C#/.NET symbol extraction in C# where practical
- add path, line, symbol kind, and dependency edges

Phase 4: RAG Reranking

- combine keyword search, semantic search, project map signals, symbols, memory, Git recency, and file-change context
- record why a result was selected
- show evidence such as "read this file because it exports LoginForm and is imported by signup.ts"

Phase 5: Memory and Confidence

- turn stable project traits into user-approved memory candidates
- use error-history memory when similar failures recur
- surface confidence and missing-context warnings in the desktop UI
- add memory expiration, usefulness scoring, duplicate merging, contradiction detection, and periodic GC
- make current workspace evidence stronger than remembered facts

Phase 6: Worker-Guided Creation

- convert `scaffoldRecommendations` into concrete `WorkerPlan` candidates
- start with React/Next.js, FastAPI, Java/Spring, Rust crate, C++ CMake, and SQL migration flows
- keep plan generation read-only and execution bounded to approved plan files
- re-run language workers after execution to confirm expected symbols, routes, tests, and commands exist

Phase 7: External Browser Verification

- detect and run project-owned Playwright verification
- attach report, trace, screenshot, console, and network evidence
- classify Playwright failures and feed them into the repair-plan loop
- run screenshot capture and deterministic visual heuristics after Playwright flow checks
- add optional LLM screenshot review for ambiguous visual regressions
- stop repair loops on repeated identical visual failure signatures

## Korean Summary

AgentQ의 본체는 C#/.NET으로 유지한다. 대신 JavaScript/TypeScript, Python, C++, Go, Rust, Unity/Unreal 같은 언어별 깊은 분석은 각 생태계의 도구를 쓰는 worker로 분리한다.

이 구조를 쓰면 AgentQ는 Windows 데스크톱 앱, Memory, Evidence, Git, RAG 통합을 안정적으로 담당하고, 언어별 worker는 프로젝트 구조, symbol, import/dependency graph, build/test 명령을 JSON으로 반환한다.

우선은 C# 기반 Multi-language Project Map v1을 만들고, 이후 TypeScript/Python/C++ worker부터 확장하는 방향이 현실적이다.
