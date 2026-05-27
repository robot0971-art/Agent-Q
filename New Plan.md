# AgentQ Current Plan

Updated: 2026-05-27

Implementation rule: finish items from top to bottom. When an item is implemented, verified, committed, and pushed, remove it from this file.

## Current Completion Estimate

Current estimated product completeness:

- Engine/core agent capability: 82-84%
- Desktop product UX: 78-80%
- Release/distribution readiness: 65-68%
- Overall practical readiness: about 80%

The main engine pieces are now present: Project Map, hybrid search, symbol search, language workers, Roslyn analysis, evidence trail, confidence signals, memory, replay/eval, Git workflow, visual evidence, Unity analysis, planning, MCP foundation, Auto Fix review flow, run summary, project dashboard, and plan/evidence/eval connection.

AgentQ has reached the current 80% target for the internal Windows beta path: the demo-driven issues found so far are fixed or documented, visual evidence is covered by regression tests, release readiness is documented, and the core build/test/format gates are green.

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
- Completed the 80% readiness review:
  - confirmed the main desktop workflow is documented as `open project -> analyze -> ask -> edit -> verify -> review -> commit`
  - confirmed the three demo scenarios are repeatable in `docs/demo-scenarios.md`
  - confirmed the demo run log covers C#, React/TypeScript, Unity analysis, and Unity visual evidence regression results
  - verified formatting with `dotnet format .\csharp\AgentQ.sln --verify-no-changes --no-restore`
  - verified Release build with `dotnet build .\csharp\AgentQ.sln -c Release --no-restore /t:Rebuild` (0 warnings, 0 errors)
  - verified local build and tests with `.\build.ps1` and `.\test.ps1` (256 passed, 0 failed, 0 skipped)
  - kept the manual desktop file-picker visual evidence smoke test as a pre-release checklist item rather than claiming it is automated
- Started the Windows 1.0 stabilization pass:
  - added `release-readiness.ps1` as a single local preflight for format, Release rebuild, wrapper build, non-integration tests, and optional clean working-tree verification
  - updated the release checklist and README to use the preflight before beta tagging or publishing
  - verified the preflight with `.\release-readiness.ps1 -SkipGitStatus` (257 passed, 0 failed, 0 skipped)
- Completed the persistent MCP session pass:
  - changed `StdioMcpClient` to reuse initialized stdio MCP server processes within the client lifetime instead of restarting for `tools/list` and each `tools/call`
  - added JSON-RPC response routing with per-request completions, request IDs, cancellation, timeout handling, and process-exit failure propagation
  - kept bridge tools sharing a single MCP client instance during Desktop tool registry setup
  - added a stateful stdio MCP regression test proving `tools/list` and repeated `tools/call` operations stay on the same initialized session
- Completed the release trust status pass:
  - confirmed the release workflow generates SHA256 checksum sidecars for installer, portable ZIP, and CLI package artifacts
  - confirmed README and release readiness docs explicitly explain unsigned Windows builds and expected SmartScreen/browser warnings
  - kept code signing deferred until certificate/HSM/cloud signing budget is available
- Started the stronger sandbox and permission policy pass:
  - aligned Desktop permission classification with BashTool hard blocks for destructive Git recovery commands and recursive/system-level shell commands
  - blocked `git clean -xfd`, destructive `git restore`, destructive `git checkout`, encoded PowerShell commands, and forced recursive deletes through desktop policy assessment
  - added regression coverage for destructive recovery command blocking
  - verified with `.\release-readiness.ps1 -SkipGitStatus` (264 passed, 0 failed, 0 skipped)

## Active Work Queue After 80%

1. Windows 1.0 Stabilization
   - Run `.\release-readiness.ps1` on a clean working tree.
   - Run the release readiness checklist on a clean Windows machine or VM.
   - Perform the manual desktop file-picker visual evidence smoke test.
   - Exercise file change review, approve/reject/revert, snapshot rollback, memory operations, and telemetry dashboard refresh.
   - Fix any beta-blocking UX confusion or mojibake found during that pass.

## Later 80% -> 90% Candidates

These are important, but should come after the Windows 1.0 stabilization pass unless a demo exposes them as blockers.

1. Stronger Sandbox And Permission Policy
   - Make shell/file/git permission boundaries clearer in the desktop UI.
   - Consider a structured capability/policy engine if regex-based policy becomes hard to reason about.

2. Error History And Failure Memory
   - Strengthen recurring failure fingerprints.
   - Surface previously seen failures more directly in Auto Fix and Verify.

3. Context Compression And Tool Router
   - Improve large-project context summaries.
   - Make tool choice more explicit and measurable.

4. Cross-Platform Strategy
   - Keep Windows WPF for near-term 1.0.
   - Reduce Windows-specific service coupling.
   - Start Avalonia prototype only after Windows demo flows are stable.

5. Code Signing Pipeline
   - Revisit when paid certificate/HSM/cloud signing budget is available.
   - Automate signing for desktop executable and installer in CI.
