# Hermes Import Notes

Imported from `C:\Users\admin\Desktop\hermes-agent-main` on 2026-06-05.

## Brought In

- Converted selected Hermes workflow skills into Agent-Q project skills under `.agentq/skills/`.
- Adapted the subagent review loop into an Agent-Q-friendly implement, spec-review, quality-review procedure.
- Adapted the code wiki workflow for C# solution/module documentation.
- Adapted the REST/GraphQL API debugging workflow for provider and HTTP integration work.
- Repaired Agent-Q greenfield scaffold Korean triggers so Korean requests activate the right skill.

## Not Brought In

- Hermes Python runtime, gateway, messaging platforms, cron, and dashboard code were not copied because Agent-Q is a C#/.NET WPF desktop and CLI project.
- Hermes `locales/ko.yaml` was not copied because the local file is mojibake-corrupted and would degrade Agent-Q's existing Korean UI strings.
- Hermes web/React desktop UI assets were not copied because Agent-Q already has WPF views and view models.
- Hermes provider plugins were not copied as code; Agent-Q already has provider abstraction and OpenAI-compatible support. The useful idea is the provider catalog/registry pattern, not the Python implementation.

## Future Candidates

- Add an Agent-Q provider catalog source that can ingest a curated JSON model catalog.
- Add scheduled automation support inspired by Hermes cron only after Agent-Q has a clear desktop scheduling UX.
- Add skill browsing or skill management UI for `.agentq/skills`.
