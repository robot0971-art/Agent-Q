# AgentQ Demo Run Log

## 2026-05-26 - Scenario 1: C# Bug Fix With Verification

Sample workspace:

- `C:\Users\admin\Desktop\AgentQ-Demo-CSharp`

Request shape:

```text
Find and fix the failing parser test. Keep the change minimal and run the focused verification.
```

Evidence gathered:

- `rg "KeyValueParser|Parse\(" C:\Users\admin\Desktop\AgentQ-Demo-CSharp -n -g "*.cs"`
- `DemoParser\KeyValueParser.cs`
- `DemoParser.Tests\KeyValueParserTests.cs`

Initial verification:

```powershell
dotnet test "C:\Users\admin\Desktop\AgentQ-Demo-CSharp\AgentQDemoParser.slnx" --filter "FullyQualifiedName~KeyValueParserTests"
```

Result:

- Failed as expected.
- `Parse_RemovesWrappingQuotesFromValues` expected `AgentQ` but received `"AgentQ"`.

Fix:

- Minimal parser change in `DemoParser\KeyValueParser.cs`.
- Trim whitespace, then remove wrapping double quote characters from parsed values.

Final verification:

```powershell
dotnet test "C:\Users\admin\Desktop\AgentQ-Demo-CSharp\AgentQDemoParser.slnx" --filter "FullyQualifiedName~KeyValueParserTests"
```

Result:

- Passed: 2
- Failed: 0
- Skipped: 0

Findings:

- Build/test scripts in AgentQ did not restore new package references before isolated project builds. This blocked the baseline build after the Roslyn analysis dependency was added.
- Fixed by adding explicit restore steps to `build.ps1`, `build.cmd`, `test.ps1`, and `test.cmd`.
- Desktop UI partial pass completed:
  - AgentQ Desktop launched from `csharp\AgentQ.Desktop\bin\Debug\net10.0-windows\AgentQ.Desktop.exe`.
  - Project panel accepted `C:\Users\admin\Desktop\AgentQ-Demo-CSharp`.
  - `Analyze` detected `.NET / net10.0`.
  - Project dashboard showed `dotnet build`, `dotnet test`, `AgentQDemoParser.slnx`, C# projects, key symbols, Roslyn symbols, and project references.
  - After initializing the disposable sample as a git repo, Project dashboard detected branch `master`.
  - Git panel `Status` detected `DemoParser/KeyValueParser.cs` as the only source change after adding `.gitignore` to the sample baseline.
  - Git panel `Diff` showed `DemoParser/KeyValueParser.cs | 2 +-` and `1 file changed, 1 insertion(+), 1 deletion(-)`.
- Demo setup issue found:
  - The initial disposable sample git baseline accidentally included `bin/` and `obj/`, making Git panel output noisy after test runs.
  - Fixed in the disposable sample by adding `.gitignore` for `bin/`, `obj/`, and `TestResults/`, then amending the sample baseline.
- Remaining Desktop UI pass:
  - Run the actual chat request through AgentQ Desktop with a configured provider.
  - Confirm Evidence, Verify, Change preview, Plan, Run summary, and Git commit-summary behavior from the app-generated run.

## 2026-05-26 - Scenario 1: Provider-Backed Desktop Chat Pass

Setup:

- Reset `C:\Users\admin\Desktop\AgentQ-Demo-CSharp` to the failing parser baseline.
- Started AgentQ Desktop against the sample workspace.
- Increased desktop request timeout from 30 seconds to 180 seconds for the interactive approval run.

Request:

```text
Find and fix the failing parser test. Keep the change minimal and run the focused verification.
```

Result:

- AgentQ Desktop used the configured `opencode-go` provider.
- The run requested approval for `dotnet test` in Coding mode.
- After approval, AgentQ:
  - ran tests and identified `Parse_RemovesWrappingQuotesFromValues`
  - read `DemoParser\KeyValueParser.cs`
  - changed only `DemoParser\KeyValueParser.cs`
  - reran focused verification
  - detected that `--no-build` did not rebuild changed source
  - reran tests with build
  - reported all 2 tests passing

Final sample diff:

- `DemoParser/KeyValueParser.cs | 7 ++++++-`
- `1 file changed, 6 insertions(+), 1 deletion(-)`

Product fixes made during this pass:

- Added startup workspace support:
  - first command-line argument
  - `AGENTQ_DESKTOP_WORKSPACE` environment variable
- Updated Git panel commands to exclude AgentQ internal `.agentq/` files from status, diff stat, full diff, and changed-file lists.

Verification:

- `.\build.ps1` passed.
- `.\test.ps1` passed: 242 non-integration tests.
- Relaunched AgentQ Desktop with `C:\Users\admin\Desktop\AgentQ-Demo-CSharp` as the first argument.
- Confirmed Git panel showed only `DemoParser/KeyValueParser.cs` even though `.agentq/replay` existed.

Findings:

- The default 30 second request timeout is too short for provider-backed demo runs that include approval time and test execution.
- Shell-run verification appears in the agent answer and evidence, but the Verify panel still displays `Not verified`; Verify cards are only created through the explicit Verify workflow.
- The Run summary correctly showed completion and one changed file after the provider-backed run.

Follow-up fix:

- Added a shell verification bridge that promotes successful verification-like `bash` tool runs into Verify result cards.
- Currently detects successful shell runs for commands such as `dotnet test`, `npm test`, `npm run build`, and lint/build variants when the command exits with code 0 and the output contains a conservative success marker.
- Added regression tests for:
  - passed `dotnet test` creates a Verify card
  - localized Korean `dotnet test` success output creates a Verify card
  - failed `dotnet test` does not create a passed card
  - non-verification shell commands do not create Verify cards
- Verified with `.\build.ps1` and `.\test.ps1`; 246 non-integration tests passed.

UI confirmation:

- Re-ran the provider-backed Desktop chat pass against the failing sample baseline.
- AgentQ fixed `DemoParser/KeyValueParser.cs` again.
- Verify panel displayed `PASSED: dotnet test`.
- Verify card showed:
  - command: `dotnet test`
  - status: `PASSED`
  - summary: `Shell verification passed during the agent run.`
  - detail: `Verification completed successfully.`
- Run summary showed `Completed`, `Review changed files, then run verification.`, and `1 changed`.
- Focused sample verification passed afterward: 2 passed, 0 failed.

Remaining minor UI note:

- A stale `ERROR` label can remain visible in broad UI Automation text collection after earlier failed/timeout attempts, even when the current run is completed and Verify shows `PASSED`.

## 2026-05-26 - Scenario 2: React/TypeScript Feature Change

Sample workspace:

- `C:\Users\admin\Desktop\AgentQ-Demo-React`

Setup:

- Created a Vite React TypeScript sample app.
- Replaced the starter screen with an operational dashboard queue.
- Baseline intentionally had an empty `dashboardItems` array but no empty state UI.
- Initialized the disposable sample as a git repository.
- Baseline verification passed:
  - `npm run lint`
  - `npm run build`

Request:

```text
Add a compact empty state to the dashboard list using the existing component style. Find the relevant component first.
```

Result:

- Project dashboard detected `Node / Vite, React, TypeScript`.
- Project dashboard showed:
  - `npm run build`
  - `npm run lint`
  - `component DashboardList (src/App.tsx)`
  - `component App (src/App.tsx)`
  - TypeScript worker signals for React components and imports.
- AgentQ used `symbol_search` first and identified `DashboardList`.
- AgentQ read `src/App.tsx` and `src/App.css`.
- AgentQ changed only:
  - `src/App.tsx`
  - `src/App.css`
- Added a compact empty state:
  - `No active items`
  - `Queue is empty`
  - dashed border, 6px radius, muted text, and existing dashboard spacing.

External verification:

- `npm run lint` passed.
- `npm run build` passed.

Findings:

- Scenario 2 provider-backed run timed out after producing a useful implementation and summary.
- Verify card did not appear for the first frontend run because Vite reports success as `built in ...`, which was not in the shell verification detector.
- A follow-up verification prompt using `npm run build` exposed that shell commands need to default to the selected workspace as their working directory; otherwise provider-generated commands without `cd` can run from the desktop binary folder.

Product fixes made during this pass:

- `BashTool` now uses `AGENTQ_WORKSPACE_ROOT` as the process working directory when available.
- Shell verification detector now recognizes Vite build success output containing `built in`.
- Added regression tests for:
  - `BashTool` uses `AGENTQ_WORKSPACE_ROOT` as cwd
  - Vite `npm run build` output creates a Verify card

Verification:

- `.\build.ps1` passed.
- `.\test.ps1` passed: 248 non-integration tests.

UI confirmation:

- Re-ran a short provider-backed verification prompt:

  ```text
  Run npm run build to verify the current React change. Do not edit files.
  ```

- AgentQ requested approval for the build command.
- Verify panel displayed `PASSED: frontend build`.
- Verify card showed:
  - command: `cd "C:\Users\admin\Desktop\AgentQ-Demo-React" ; npm run build`
  - status: `PASSED`
  - summary: `Shell verification passed during the agent run.`
  - detail: `Verification completed successfully.`
- This confirms the Bash working-directory fix and Vite `built in ...` detector fix work in the Desktop UI.

## 2026-05-26 - Scenario 3: Unity Project Analysis

Sample workspace:

- `C:\Users\admin\Desktop\AgentQ-Demo-Unity`

Setup:

- Created a lightweight Unity fixture with:
  - `Assets/Scenes/DemoBattle.unity`
  - `Assets/Prefabs/DamageFlash.prefab`
  - `Assets/Scripts/DamageFlashController.cs`
  - `Assets/Scripts/EnemyHealth.cs`
  - `Assets/Scripts/Game.asmdef`
  - `Packages/manifest.json`
  - `ProjectSettings/ProjectVersion.txt`
- Initialized the fixture as a disposable git repository.
- No screenshot/video was attached for this pass; visual evidence remains untested.

Project dashboard result:

- Detected `Unity / Unity 6000.2.8f1, Unity Input System, Unity URP`.
- Showed `Unity Test Runner` as the verification hint.
- Mapped Unity project structure:
  - Unity assets: `Assets`
  - Unity packages: `Packages`
  - Unity project settings: `ProjectSettings`
  - Unity scenes: `Assets/Scenes/DemoBattle.unity`
  - Unity prefabs: `Assets/Prefabs/DamageFlash.prefab`
  - Unity scripts: `Assets/Scripts/DamageFlashController.cs`, `Assets/Scripts/EnemyHealth.cs`
  - Unity asmdefs: `Assets/Scripts/Game.asmdef`
- Key symbols included:
  - `DamageFlashController`
  - `EnemyHealth`
  - `DamageFlashController.PlayFlash`
  - `DamageFlashController.ResetColor`
  - `EnemyHealth.TakeDamage`
- Dependency graph included:
  - Unity packages from `Packages/manifest.json`
  - `UnityEngine` usings
  - asmdef assembly `AgentQ.Demo.Game`

Request:

```text
Analyze why the damage feedback effect may be hard to notice. Inspect the relevant Unity scripts and propose a small scoped fix, but do not edit files yet.
```

Result:

- AgentQ read relevant Unity files including the damage flash script, prefab, and scene.
- AgentQ made no file changes, as requested.
- AgentQ identified likely issues:
  - `flashDuration` is only `0.12` seconds.
  - Flash snaps back instantly instead of fading.
  - Reset uses hardcoded `Color.white` instead of preserving the original sprite tint.
  - `Invoke` timing is less robust than a coroutine for frame-smooth feedback.
- AgentQ proposed a scoped fix in `DamageFlashController.cs`:
  - store original color
  - use a coroutine
  - interpolate from flash color back to original color
  - slightly increase perceived duration

Findings:

- Unity project map detection worked well on the lightweight fixture.
- Provider-backed Unity analysis completed and produced a scoped plan without editing files.
- Visual evidence attachment handling still needs a separate pass with an actual screenshot or video.
- The stale `ERROR` UI text issue also appeared here after prior runs, even though the current Unity analysis completed.
