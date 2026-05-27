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
- Completed the release readiness documentation pass:
  - updated README with the current desktop beta workflow and known limitations
  - added `docs/release-readiness.md`
  - included installer, portable ZIP, CLI package, checksum, smoke-test, code-signing, and release-notes checklists
- Completed the safe refactor guardrails pass:
  - blocked high-risk whole-file rewrites for existing large, Unity, serialized asset, and core files unless explicitly acknowledged
  - blocked broad `replace_all` or large replacements on high-risk files unless explicitly acknowledged
  - kept small patch-sized edits available for Unity `MonoBehaviour` files
  - added Desktop prompt guidance for Unity refactors, `[SerializeField]` preservation, phased compilation, and destructive restore caution
- Completed the edit failure recovery pass:
  - tracked repeated `write_file` and `edit_file` failures by file and strategy during a run
  - stopped retrying the same failed edit strategy after repeated failures
  - returned recovery guidance to reread the file, compare intended shape, and continue with smaller patches before restore/copy-paste fallbacks
- Completed the unsafe editing eval signals pass:
  - surfaced repeated edit failures, high-risk rewrite blocks, manual copy-paste, and destructive restore signals in the Eval Dashboard findings
  - added replay-backed tests for unsafe editing findings

## Active Work Queue Toward 80%

1. 80% Readiness Review
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
