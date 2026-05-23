# AgentQ Current Plan

Updated: 2026-05-23

Implementation rule: finish items from top to bottom. When an item is implemented, verified, committed, and pushed, remove it from this file.

## Current Focus

AgentQ has a solid v1.5 engine foundation: workspace analysis, Project Map, hybrid search, dependency graph, language workers, Roslyn analysis, evidence, confidence, memory, replay/eval, Git workflow, visual evidence, Unity analysis, and multi-agent role planning.

The next work should raise the perceived product completeness from roughly 50-60% toward 70% by turning those engine pieces into a smooth daily-use workflow:

- make the main path obvious: open project -> analyze -> ask -> edit -> verify -> review -> commit
- reduce scattered panels and repeated manual steps
- make state, evidence, verification, and next actions visible without requiring the user to understand internals
- improve empty/error states so the app feels stable even when tools fail
- prioritize demoable end-to-end scenarios over adding more isolated engine features

## Active Work Queue

1. End-to-End Run UX
   - Add a clear run summary that shows current phase, next action, last evidence, verification status, and commit readiness.
   - Make the happy path visible from one place: request -> context gathered -> files changed -> verification -> review -> commit.
   - Add empty states and user-facing guidance for idle, running, failed, and completed runs.

2. Project Dashboard Integration
   - Consolidate Project Map, key files, key symbols, dependencies, confidence, evidence, and eval signals into a more useful project overview.
   - Surface what AgentQ knows about this project without requiring the user to inspect multiple tabs.
   - Add quick actions for refresh analysis, build index, open important files, and view relevant evidence.

3. Auto Workflow: Fix -> Verify -> Review
   - Improve the automatic flow after code edits: select verification, run it, classify failure, suggest or perform a retry, then pause for review.
   - Make changed-file review and verification results guide the next action.
   - Keep approval/permission boundaries clear.

4. Plan / Evidence / Eval Connection
   - Tie plan items, evidence timeline, replay/eval dashboard, and confidence into one coherent story for a run.
   - Show why an item is done or blocked using evidence and verification results.
   - Make replay/eval findings actionable instead of purely informational.

5. Demo Scenarios
   - Create or document three repeatable demo flows:
     - C# bug fix with verification
     - React/TypeScript feature change with project-aware search
     - Unity project analysis with visual/game evidence
   - Include expected commands, expected UI states, and success criteria.

6. Product Polish And Stability
   - Improve confusing copy, mojibake text, empty panels, and error messages.
   - Tighten UI spacing and scanability in the main workflow panels.
   - Add focused regression tests for the polished flows.
