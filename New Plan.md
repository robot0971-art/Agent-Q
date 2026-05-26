# AgentQ Current Plan

Updated: 2026-05-25

Implementation rule: finish items from top to bottom. When an item is implemented, verified, committed, and pushed, remove it from this file.

## Current Completion Estimate

Current estimated product completeness:

- Engine/core agent capability: 75-80%
- Desktop product UX: 70-75%
- Release/distribution readiness: 55-60%
- Overall practical readiness: about 70%

The main engine pieces are now present: Project Map, hybrid search, symbol search, language workers, Roslyn analysis, evidence trail, confidence signals, memory, replay/eval, Git workflow, visual evidence, Unity analysis, planning, MCP foundation, Auto Fix review flow, run summary, project dashboard, and plan/evidence/eval connection.

The next goal is to move AgentQ from roughly 70% to 80% by proving the main workflows with repeatable demos, fixing demo-discovered issues, and preparing a clean release pass.

## Completed In This Pass

- Pulled and reviewed upstream changes from `0458bdd` to `329030e`.
- Verified the pulled changes with build and tests.
- Added always-visible Run Summary.
- Added Project Dashboard integration.
- Improved Auto Fix -> Review -> Verify flow.
- Connected Plan, Evidence, Verification, and Eval signals.
- Added repeatable demo scenario documentation.
- Improved product polish:
  - restored the Korean desktop plan document
  - improved empty states and next-action copy
  - fixed build/test scripts to include Desktop and run the correct `net10.0-windows` test assembly
- Verified with `.\build.ps1` and `.\test.ps1`.
- 2026-05-26:
  - Pulled latest `main` from GitHub.
  - Fixed build/test scripts to restore packages before isolated project builds.
  - Verified with `.\build.ps1` and `.\test.ps1`.
  - Committed and pushed `705f661` (`Fix build scripts to restore packages`).
  - Ran Scenario 1 core CLI flow against disposable sample `C:\Users\admin\Desktop\AgentQ-Demo-CSharp`.
  - Ran Scenario 1 partial Desktop UI pass:
    - Project dashboard detected `.NET / net10.0`, `dotnet build`, `dotnet test`, `.slnx`, C# projects, symbols, Roslyn symbols, and project references.
    - Git panel detected branch `master`, one source change, and a focused one-file diff after the disposable sample was cleaned up with `.gitignore`.
  - Logged the Scenario 1 run in `docs/demo-run-log.md`.

## Active Work Queue Toward 80%

1. Run Demo Scenario 1: C# Bug Fix With Verification
   - Use `docs/demo-scenarios.md`.
   - Run the flow against a disposable C# sample project or branch.
   - Confirm Project dashboard, Evidence, Verify, Change preview, Git, and Run summary all behave as expected.
   - Complete the remaining provider-backed Desktop chat pass; the core CLI bug-fix/focused verification flow and Project dashboard/Git UI checks passed on 2026-05-26.
   - Log any UX or functional issue found during the run.

2. Run Demo Scenario 2: React/TypeScript Feature Change
   - Use a small React/Vite/Next TypeScript sample project.
   - Confirm TypeScript worker/project-aware search signals.
   - Verify frontend command selection and Evidence trail.
   - Log any search, verification, or UI issue found.

3. Run Demo Scenario 3: Unity Project Analysis
   - Use a Unity sample project or representative fixture.
   - Confirm Unity project map entries: scenes, prefabs, scripts, asmdefs, packages.
   - Attach a screenshot/video if available and confirm visual evidence appears.
   - Log any Unity-specific analysis or verification gaps.

4. Demo Issue Fix Pass
   - Fix only issues discovered from the three demo runs.
   - Prefer small, focused changes over new feature expansion.
   - Add regression tests for any behavior that broke or confused the demo flow.
   - Verify with `.\build.ps1` and `.\test.ps1`.

5. Release Readiness Checklist
   - Update README with current desktop workflow and known limitations.
   - Add release notes draft for the current beta.
   - Add installer/portable ZIP QA checklist.
   - Keep code signing as a later paid-release decision unless a certificate is already available.

6. 80% Readiness Review
   - Confirm the main path works:
     `open project -> analyze -> ask -> edit -> verify -> review -> commit`
   - Confirm all three demo scenarios are repeatable.
   - Confirm build/test are green.
   - Confirm no obvious mojibake, stale empty states, or confusing main-panel copy remains.
   - Re-estimate completeness and decide the next pass:
     - Windows 1.0 stabilization
     - Persistent MCP Session
     - Avalonia/Linux prototype
     - Release signing pipeline

7. Safe Refactor Guardrails
   - Detect large or high-risk files before editing, especially Unity `MonoBehaviour` files with `[SerializeField]` Inspector bindings.
   - Avoid whole-file rewrites for large/core files unless explicitly approved.
   - Do not ask the user to manually copy-paste full replacement files as a normal strategy.
   - Require patch-sized edits for refactors such as `AutoBattleController` responsibility splits.
   - Add a Unity refactor checklist:
     - preserve `SerializeField` names
     - avoid adding components unless requested
     - keep prefab/Inspector assignments intact
     - compile after each phase
     - verify spawn, movement, attack, death, reward, boss, and stage progression

8. Edit Failure Recovery
   - If an edit tool fails repeatedly, stop retrying the same strategy.
   - Read the current file and compare the intended shape before continuing.
   - Detect likely file corruption or partial rewrites.
   - Treat direct manual copy-paste instructions as a last-resort fallback only.
   - Before asking the user to paste code manually, attempt automatic recovery through backup, restore, and minimal patches.
   - If manual paste is unavoidable, explain why tool-based editing is unsafe or unavailable.
   - Before suggesting `git checkout -- <file>` or `git restore <file>`, require:
     - `git diff -- <file>`
     - a backup copy when useful
     - a clear warning that local changes to that file will be discarded
   - Prefer restore + minimal patches over replacing an entire complex file.

9. Unsafe Editing Eval Signals
   - Record repeated edit failures, partial rewrite attempts, manual copy-paste fallbacks, and destructive restore suggestions.
   - Surface these as Eval Dashboard findings.
   - Add tests or replay fixtures for failed large-file refactor attempts.
   - Use those signals to improve future tool-routing and recovery behavior.

## Later 80% -> 90% Candidates

These are important, but should come after the demo-driven 80% pass unless a demo exposes them as blockers.

1. Persistent MCP Session
   - Replace per-call MCP process startup with reusable sessions.
   - Add JSON-RPC response routing with `TaskCompletionSource`.
   - Add timeout, cancellation, process death recovery, and tests.

2. Stronger Sandbox And Permission Policy
   - Make shell/file/git permission boundaries clearer.
   - Improve destructive command prevention and user-facing explanations.
   - Include the destructive restore guard from the safe refactor work.

3. Error History And Failure Memory
   - Strengthen recurring failure fingerprints.
   - Surface previously seen failures more directly in Auto Fix and Verify.

4. Context Compression And Tool Router
   - Improve large-project context summaries.
   - Make tool choice more explicit and measurable.

5. Cross-Platform Strategy
   - Keep Windows WPF for near-term 1.0.
   - Reduce Windows-specific service coupling.
   - Start Avalonia prototype only after Windows demo flows are stable.

6. Code Signing Pipeline
   - Revisit when paid certificate/HSM/cloud signing budget is available.
   - Automate signing for desktop executable and installer in CI.
