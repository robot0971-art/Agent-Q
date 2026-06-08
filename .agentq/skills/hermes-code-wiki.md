---
id: hermes-code-wiki-en
title: Hermes Code Wiki Adaptation
priority: 50
taskKinds: documentation,analysis
triggers: wiki,architecture,docs,map,overview,structure
excludes: quick-fix,one-line
---
# Code Wiki

Use this skill to create or update architecture notes for AgentQ.

## Procedure

1. Start from actual files, not memory.
2. Map the main user flow before listing implementation details.
3. Keep each section grounded in file paths and class names.
4. Separate facts from recommendations.
5. Keep the document useful for future implementation work.

## AgentQ Focus Areas

- Desktop workflow services and view models.
- `DesktopAgentService` agent loop and deterministic desktop services.
- Tool permission, file mutation snapshots, scaffold creation, verification, replay, and diagnostics.
- Tests that lock behavior.
