# Agent Q Document Status

This file separates current design sources from historical notes so automatic context and RAG do not treat old design documents as equal authority.

## Current Sources

- `AGENTS.md` - project operating rules and architectural principles.
- `docs/TODO.md` - active audit checklist and verification history.
- `docs/Agent Q.md` - current runtime architecture summary, including completed TurnState routing boundary status, A1 implementation completion pipeline, and verification evidence.
- `docs/llm-first-agent-milestones.ko.md` - current LLM-first routing milestones and remaining smoke-test notes.
- `docs/DOCUMENT_STATUS.md` - this document-status map.

## Archived References

Files under `docs/archive/` are historical references. They may still be useful when the user explicitly asks for old release, demo, RAG, or worker notes, but they should not override the current sources above.

- `docs/archive/DEVELOPMENT_PLAN.md` - superseded TurnState planning notes.
- `docs/archive/AgentQ.Desktop.Plan.ko.md` - early desktop product plan.
- `docs/archive/agent-runtime-architecture-notes.md` - older runtime notes.
- `docs/archive/demo-run-log.md` - historical demo run log.
- `docs/archive/demo-scenarios.md` - historical demo scenarios.
- `docs/archive/embedding-rag-design.md` - early RAG design notes.
- `docs/archive/language-worker-architecture.md` - language worker design notes.
- `docs/archive/release-readiness.md` - historical release checklist.

## Delete Candidates

No markdown file is deleted in this pass. Delete only after confirming no README, release, or user workflow still references it.
