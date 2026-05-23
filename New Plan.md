# AgentQ Current Plan

Updated: 2026-05-23

Implementation rule: finish items from top to bottom. When an item is implemented, verified, committed, and pushed, remove it from this file.

## Current Focus

AgentQ has a working v1 desktop agent core. The next work should upgrade codebase understanding, retrieval quality, evidence, and confidence so the agent can handle larger projects with fewer guesses.

## Active Work Queue

1. Hybrid Search v2: graph-aware ranking
   - Feed dependency/import graph signals into `hybrid_search`.
   - Rank files higher when they define matching symbols, import matching files, or are imported by matching files.
   - Include recent Git changes and project memory matches as secondary ranking signals.
   - Return clear ranking reasons.

2. Evidence v2: stronger "why this file" explanations
   - Show reasons such as symbol match, imported by another file, imports a relevant file, recent Git change, and project-map role.
   - Keep explanations concise enough for the desktop Evidence tab.
   - Add tests for evidence reason formatting.

3. Confidence v2: context sufficiency
   - Score whether enough related files were inspected.
   - Warn when dependency neighbors, tests, or verification commands were skipped.
   - Include graph/search/memory evidence as positive confidence signals.

4. Error History v2
   - Fingerprint build/test failures.
   - Detect recurring failures.
   - Suggest previous fixes from approved memory or error-history lessons.

5. MCP tool bridge v1
   - Turn MCP server config into a working stdio MCP client.
   - Support tool listing and tool calls.
   - Route MCP tool calls through existing permission and evidence systems.

6. Eval / Replay Dashboard v1
   - Add a desktop view for replay logs and local telemetry.
   - Show tool failures, search success, verification status, and confidence history.

7. Language Worker / AST upgrade
   - Replace lightweight graph extraction gradually with ecosystem-specific workers.
   - Start with TypeScript, then Python, then C#/Roslyn if useful.

8. Visual Agent foundation
   - Add screenshot/UI analysis workflow hooks.
   - Keep Unity scene understanding as a later specialized path.

9. Multi-Agent foundation
    - Explore Planner, Coder, Reviewer, and Tester roles only after graph/search/evidence are stronger.
