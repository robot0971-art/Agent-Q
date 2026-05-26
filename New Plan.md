# AgentQ Current Plan

Updated: 2026-05-26

Implementation rule: finish items from top to bottom. When an item is implemented, verified, committed, and pushed, remove it from this file.

## Current Completion Estimate

Current estimated product completeness:

- Engine/core agent capability: 80-82%
- Desktop product UX: 75-78%
- Release/distribution readiness: 60-65%
- Overall practical readiness: about 76%

The main engine pieces are now present: Project Map, hybrid search, symbol search, language workers, Roslyn analysis, evidence trail, confidence signals, memory, replay/eval, Git workflow, visual evidence, Unity analysis, planning, MCP foundation, Auto Fix review flow, run summary, project dashboard, and plan/evidence/eval connection.

The next goal is to move AgentQ from roughly 76% to 80% by fixing the issues discovered during real demo runs, proving visual evidence on Unity-like work, and preparing a clean release checklist.

## Completed In This Pass

- Pulled and reviewed upstream changes from `b5d9aaf` to `428f554`.
- Confirmed the pull was a clean fast-forward with no merge conflicts.
- Verified the pulled changes with `.\build.ps1`.
- Verified the pulled changes with `.\test.ps1`:
  - 250 passed
  - 0 failed
  - 0 skipped
- Confirmed the three demo scenarios are now represented in `docs/demo-run-log.md`:
  - C# bug fix with verification
  - React/TypeScript feature change with frontend verification
  - Unity project analysis
- Confirmed the latest pull added or improved:
  - shell-run verification cards for successful `dotnet test`, localized Korean test output, and Vite/frontend build output
  - `BashTool` workspace-root execution through `AGENTQ_WORKSPACE_ROOT`
  - provider configuration secret protection
  - MCP server registry hardening
  - CI warnings-as-errors, format checks, coverage upload, and release checksums
  - explicit restore in build/test scripts
  - `.dotnet/` cache cleanup and ignore rules
  - repository formatting normalization
- Fixed the stale static `ERROR` UI text discovered during demo runs:
  - removed the always-visible status legend from the desktop side panel
  - kept real failed verification cards and failure states untouched
  - added a regression test to prevent static `Text="ERROR"` from returning to `MainWindow.xaml`
  - verified with `.\build.ps1`
  - verified with `.\test.ps1` (251 passed, 0 failed, 0 skipped)
- Completed the Unity visual evidence attachment code-level pass:
  - confirmed visual attachments are added to the Evidence timeline
  - improved Plan evidence so visual attachment context stays visible alongside later file/tool evidence
  - added a regression test using a Unity-style `damage-flash.png` visual evidence case
  - logged the result in `docs/demo-run-log.md`
  - verified with `.\build.ps1`
  - verified with `.\test.ps1` (252 passed, 0 failed, 0 skipped)

## Active Work Queue Toward 80%

1. Release Readiness Checklist
   - Update README with the current desktop workflow and known limitations.
   - Add a release notes draft for the current beta.
   - Add installer/portable ZIP QA checklist.
   - Keep code signing as a later paid-release decision unless a certificate or signing service is already available.

2. Safe Refactor Guardrails
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

3. Edit Failure Recovery
   - If an edit tool fails repeatedly, stop retrying the same strategy.
   - Read the current file and compare the intended shape before continuing.
   - Detect likely file corruption or partial rewrites.
   - Treat direct manual copy-paste instructions as a last-resort fallback only.
   - Before asking the user to paste code manually, attempt automatic recovery through backup, restore, and minimal patches.
   - Before suggesting `git checkout -- <file>` or `git restore <file>`, require:
     - `git diff -- <file>`
     - a backup copy when useful
     - a clear warning that local changes to that file will be discarded
   - Prefer restore plus minimal patches over replacing an entire complex file.

4. Unsafe Editing Eval Signals
   - Record repeated edit failures, partial rewrite attempts, manual copy-paste fallbacks, and destructive restore suggestions.
   - Surface these as Eval Dashboard findings.
   - Add tests or replay fixtures for failed large-file refactor attempts.
   - Use those signals to improve future tool-routing and recovery behavior.

5. 80% Readiness Review
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
