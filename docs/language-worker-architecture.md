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

## Korean Summary

AgentQ의 본체는 C#/.NET으로 유지한다. 대신 JavaScript/TypeScript, Python, C++, Go, Rust, Unity/Unreal 같은 언어별 깊은 분석은 각 생태계의 도구를 쓰는 worker로 분리한다.

이 구조를 쓰면 AgentQ는 Windows 데스크톱 앱, Memory, Evidence, Git, RAG 통합을 안정적으로 담당하고, 언어별 worker는 프로젝트 구조, symbol, import/dependency graph, build/test 명령을 JSON으로 반환한다.

우선은 C# 기반 Multi-language Project Map v1을 만들고, 이후 TypeScript/Python/C++ worker부터 확장하는 방향이 현실적이다.
