# AgentQ Current Plan

Updated: 2026-05-23

Implementation rule: finish items from top to bottom. When an item is implemented, verified, committed, and pushed, remove it from this file.

## Current Focus

AgentQ now has a working v1 desktop agent core with Project Map, dependency graph, graph-aware hybrid search, richer evidence, confidence scoring, memory, Git workflow, replay logs, and recurring failure fingerprints.

The next work should turn AgentQ into a stronger C#-centered multi-language agent:

- keep Desktop/Core, permission, memory, evidence, confidence, Git, and workflow orchestration in C#
- use language workers where ecosystem tools are better than hand-written C# analysis
- grow worker-backed AST/import/dependency analysis gradually
- make integrations such as MCP and replay review usable from the desktop app

## Active Work Queue

1. C# Roslyn analysis upgrade
   - Replace or supplement regex symbol extraction with Roslyn where practical.
   - Extract namespaces, types, methods, references, project references, and diagnostics.
   - Feed Roslyn symbols and references into Project Map, Hybrid Search, Evidence, and Confidence.

2. Eval / Replay Dashboard v1
   - Add a desktop view for replay logs and local telemetry.
   - Show tool failures, search success, verification status, confidence history, and recurring failure fingerprints.
   - Make it easy to inspect why a run succeeded, failed, or lacked enough context.

3. C++ / Go / Rust worker foundations
   - Add lightweight worker contracts before deep implementation.
   - C++: detect compile_commands.json, CMake targets, clangd/tree-sitter path.
   - Go: use go list/go packages where available.
   - Rust: use cargo metadata for crates, targets, and workspace graph.

4. Visual Agent foundation
   - Add screenshot/UI analysis workflow hooks.
   - Add desktop evidence entries for inspected screenshots.
   - Keep Figma and Unity scene understanding as later specialized paths.

5. Unity / Game project analysis
   - Improve Unity project map for Assets, Packages, ProjectSettings, scenes, prefabs, scripts, and asmdef files.
   - Add game-specific verification hints where safe.
   - Prepare for visual scene/UI analysis later.

6. Multi-Agent foundation
   - Explore Planner, Coder, Reviewer, and Tester roles only after MCP, workers, replay review, and evidence are stronger.
   - Keep v1 local and deterministic before adding parallel agent behavior.
