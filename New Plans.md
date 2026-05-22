# AgentQ New Plans

Implementation rule: finish items from top to bottom, then remove completed items from this file in the same order.

## 1. TypeScript / JavaScript Worker v1
- Add an external Node-based worker interface.
- Extract package metadata, tsconfig, import graph, exported symbols, React components, pages/routes, and npm scripts.
- Feed worker results into project map, symbol index, and search ranking.

## 2. Python Worker v1
- Add a Python worker interface.
- Extract pyproject/requirements metadata, AST symbols, FastAPI routes, SQLAlchemy models, pytest targets, and import graph.
- Feed worker results into project map, symbol index, and search ranking.

## 3. Guardrails / Verification v1
- Detect changed files and choose likely verification commands.
- Run focused build/test/lint checks when appropriate.
- Classify failures and trigger a self-correction loop before reporting success.

## 4. HITL Improvements
- Improve approval hooks for risky tools.
- Show file mutation previews before write/edit actions.
- Make Stop fully abort active loops and clean pending UI state.
- Allow new user input to interrupt and redirect the active loop safely.

## 5. Snapshot / Rollback v1
- Strengthen Git/checkpoint-based rollback first.
- Save file-level patch snapshots before mutation.
- Add a user-facing revert path for recent AgentQ changes.
- Defer full VFS until the simpler rollback path is stable.

## 6. Eval / Telemetry
- Track tool success rate, search success rate, retry count, verification success, token use, response time, model/provider, and Stop count.
- Store initial metrics as local JSON/log records.
- Add dashboard UI later.

## 7. Hybrid Routing
- Classify task complexity and recommend fast/small or large/frontier model use.
- Start with user-approved switching.
- Move toward automatic routing after telemetry exists.

## 8. MCP Support
- Add MCP client support after tool routing and HITL are stable.
- Explore Unity, Unreal, Blender, and external service MCP integration.

## 9. Multi-Agent / Tool Replay
- Split Planner, Coder, Reviewer, and Tester roles only after the single-agent loop is reliable.
- Add replayable tool timelines and deterministic rerun support later.
