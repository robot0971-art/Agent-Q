# AgentQ New Plans

Implementation rule: finish items from top to bottom, then remove completed items from this file in the same order.

## 1. Eval / Telemetry
- Track tool success rate, search success rate, retry count, verification success, token use, response time, model/provider, and Stop count.
- Store initial metrics as local JSON/log records.
- Add dashboard UI later.

## 2. Hybrid Routing
- Classify task complexity and recommend fast/small or large/frontier model use.
- Start with user-approved switching.
- Move toward automatic routing after telemetry exists.

## 3. MCP Support
- Add MCP client support after tool routing and HITL are stable.
- Explore Unity, Unreal, Blender, and external service MCP integration.

## 4. Multi-Agent / Tool Replay
- Split Planner, Coder, Reviewer, and Tester roles only after the single-agent loop is reliable.
- Add replayable tool timelines and deterministic rerun support later.
