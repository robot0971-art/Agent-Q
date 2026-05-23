# AgentQ Current Plan

Updated: 2026-05-23

Implementation rule: finish items from top to bottom. When an item is implemented, verified, committed, and pushed, remove it from this file.

## Current Focus

AgentQ has a working v1 desktop agent core. The next work should upgrade codebase understanding, retrieval quality, evidence, and confidence so the agent can handle larger projects with fewer guesses.

## Active Work Queue

1. Error History v2
   - Fingerprint build/test failures.
   - Detect recurring failures.
   - Suggest previous fixes from approved memory or error-history lessons.

2. MCP tool bridge v1
   - Turn MCP server config into a working stdio MCP client.
   - Support tool listing and tool calls.
   - Route MCP tool calls through existing permission and evidence systems.

3. Eval / Replay Dashboard v1
   - Add a desktop view for replay logs and local telemetry.
   - Show tool failures, search success, verification status, and confidence history.

4. Language Worker / AST upgrade
   - Replace lightweight graph extraction gradually with ecosystem-specific workers.
   - Start with TypeScript, then Python, then C#/Roslyn if useful.

5. Visual Agent foundation
   - Add screenshot/UI analysis workflow hooks.
   - Keep Unity scene understanding as a later specialized path.

6. Multi-Agent foundation
    - Explore Planner, Coder, Reviewer, and Tester roles only after graph/search/evidence are stronger.
