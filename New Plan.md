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

No active items. The current modernization queue has been completed.
