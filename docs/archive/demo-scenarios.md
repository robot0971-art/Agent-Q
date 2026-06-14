# AgentQ Demo Scenarios

These scenarios are repeatable smoke flows for showing and validating the desktop app. They focus on the daily-use path:

`open project -> analyze -> ask -> edit -> verify -> review -> commit`

Run them on disposable sample repositories or throwaway branches. Do not use production worktrees for demos.

## Scenario 1: C# Bug Fix With Verification

### Goal

Show that AgentQ can inspect a C# codebase, identify a focused fix, record evidence, propose verification, and guide the user through review before commit.

### Setup

- Open a C# solution or project with at least one test project.
- Make sure `dotnet` is on PATH.
- Start AgentQ Desktop.
- Select the project folder in the Project panel.

### Flow

1. Click `Analyze`.
2. Confirm the Project dashboard shows:
   - project type or framework includes C#/.NET
   - key files include `.sln` or `.csproj`
   - verification commands include `dotnet test` or a focused variant
3. Send a request like:

   ```text
   Find and fix the failing parser test. Keep the change minimal and run the focused verification.
   ```

4. Watch the Run summary:
   - phase moves through context gathering, tool use, changes, and verification
   - latest evidence explains which files or commands were used
5. Open `Evidence` and confirm file reads/searches include reasons.
6. Open `Change preview`.
7. Review changed files.
8. Mark changes approved or needs edit.
9. Run the suggested verification from `Verify`, or use `Approve all & verify` if Auto Fix paused for review.
10. Open `Plan` if a plan exists and confirm Plan evidence connects the item to evidence and verification.
11. Open `Git`, refresh status/diff, generate commit summary, stage approved files, and commit.

### Expected UI State

- Run summary shows current phase, next action, verification status, and commit readiness.
- Project dashboard shows map counts and health.
- Evidence tab contains file/search/command events.
- Verify tab shows a passed or failed result card.
- Change preview shows diff lines and review status.
- Git panel shows changed files and staged commit readiness after staging.

### Success Criteria

- The fix is minimal and scoped to the failing behavior.
- At least one verification result card is created.
- If verification fails, AgentQ classifies the failure and enables fix retry.
- The final diff is reviewable from the desktop UI.
- A commit summary can be generated from the app.

## Scenario 2: React/TypeScript Feature Change With Project-Aware Search

### Goal

Show that AgentQ can map a TypeScript/React project, use project-aware search, update UI code consistently, and choose frontend verification.

### Setup

- Open a React, Vite, Next.js, or similar TypeScript project.
- Make sure `node` and the package manager are on PATH.
- Select the project folder and click `Analyze`.

### Flow

1. Confirm the Project dashboard shows React/TypeScript or frontend signals.
2. Confirm Project Map includes UI, routes, components, scripts, or package metadata.
3. Send a request like:

   ```text
   Add a compact empty state to the dashboard list using the existing component style. Find the relevant component first.
   ```

4. Watch Evidence for:
   - `symbol_search`, `hybrid_search`, or `grep_search`
   - file-read reasons that mention component, route, or package role
5. Review changed files in `Change preview`.
6. Run the selected verification command, such as:
   - `npm test`
   - `npm run build`
   - `npm run lint`
   - a focused project-specific script from Project dashboard
7. Use Git panel to inspect diff and generate a commit summary.

### Expected UI State

- Project dashboard shows TypeScript worker signals and command hints.
- Evidence tab shows search retry if the first search misses.
- Run summary recommends review, verification, or commit based on current state.
- Verify tab records frontend build/test/lint output.

### Success Criteria

- AgentQ identifies the relevant UI file before editing.
- The implementation follows existing style and component conventions.
- Verification runs through a project script.
- Diff review is possible without leaving the desktop app.

## Scenario 3: Unity Project Analysis With Visual/Game Evidence

### Goal

Show that AgentQ can recognize a Unity project, map game-specific folders, surface scenes/prefabs/scripts, and use visual evidence when attachments are provided.

### Setup

- Open a Unity project folder that contains `Assets`, `ProjectSettings`, and `Packages`.
- Optional: attach a screenshot or short video of the UI/game issue.
- Click `Analyze`.

### Flow

1. Confirm Project dashboard shows Unity framework details.
2. Confirm Project Map includes:
   - Unity scenes
   - prefabs
   - scripts
   - asmdefs when present
3. Send a request like:

   ```text
   Analyze why the damage feedback effect is hard to notice. Use the attached screenshot/video and inspect the relevant Unity scripts.
   ```

4. Watch Evidence for:
   - visual attachment evidence
   - Unity script/project file reads
   - search results tied to game logic or assets
5. Ask AgentQ to propose a small fix or implementation plan.
6. If code changes are made, review them in `Change preview`.
7. Use project-specific verification:
   - Unity editor compile check
   - Unity test runner command if available
   - script-level C# checks when available

### Expected UI State

- Project dashboard health is at least partial map.
- Evidence tab records visual evidence and Unity file context.
- Plan tab connects the selected item to visual evidence, file evidence, and verification status.
- Change preview shows C# script changes and review state.

### Success Criteria

- AgentQ identifies Unity-specific project structure.
- Visual evidence is recorded as evidence, not hidden reasoning.
- Proposed changes are scoped to relevant scripts or assets.
- Verification expectations are explicit even when Unity editor verification must be manual.

## Demo Readiness Checklist

Before recording or presenting a demo:

- Use a clean branch or disposable project copy.
- Confirm provider/model/API key settings are saved.
- Click `Analyze` before asking for edits.
- Keep `Evidence`, `Verify`, `Change preview`, `Plan`, and `Git` tabs available during the flow.
- Run `.\build.ps1` and `.\test.ps1` after desktop changes to AgentQ itself.
- Commit only after reviewing changed files and verification status.

## Known Demo Limits

- The desktop app is currently Windows-focused.
- Linux/macOS support requires an Avalonia migration or equivalent UI replacement.
- Unity verification may require manual Editor checks unless the project exposes command-line test scripts.
- Embeddings are optional; demos should still work with project map, keyword search, symbol search, and hybrid search.
