# AgentQ Current Plan

Updated: 2026-05-20

## Current Position

AgentQ is past the prototype stage. The CLI, providers, tool execution loop, session/config persistence, non-interactive automation mode, test suite, and Windows desktop shell all exist.

Current work is focused on product hardening:

- keep documentation aligned with the merged `main` branch
- continue reducing large desktop UI/code-behind files after the first settings panel extraction
- make desktop Git workflows safer and clearer
- keep provider/model metadata outside hardcoded view model state
- keep regression coverage growing around desktop services and CLI behavior

## Recently Completed

### Main branch sync after consolidation

The previous feature work has been consolidated into `main`. Local development was synced to `origin/main` on 2026-05-20, and the old remote feature branch was pruned.

Validation after the sync:

- `dotnet test .\csharp\AgentQ.sln`: `96/96` passed

### Documentation refresh

Planning docs were updated after the branch consolidation:

- `New Plan.md` now reflects the merged `main` state
- `docs/AgentQ.Desktop.Plan.ko.md` was restored as readable UTF-8 Korean text
- README validation counts were refreshed

### Tool step limit raised

The default tool-loop cap was raised from `8` to `45` in:

- `csharp/AgentQ.Cli/CliToolLoopRunner.cs`
- `csharp/AgentQ.Core/Models/ChatModels.cs`
- `csharp/AgentQ.Desktop/Services/DesktopAgentService.cs`

This reduces premature stops during broad codebase tasks while keeping an upper bound against runaway tool loops.

### CLI dependency injection refactor

The CLI now uses DI beyond the startup shell. `CliApplication` is no longer responsible for most storage, output, non-interactive execution, interactive command handling, presentation, or conversation rendering.

Runtime services include:

- `IConfigStore` / `FileConfigStore`
- `ISessionStore` / `FileSessionStore`
- `IInputFileReader` / `InputFileReader`
- `ICliAutomationOutput` / `CliAutomationOutput`
- `CliNonInteractiveRunner`
- `CliInteractivePersistenceCommands`
- `CliInteractiveSettingsCommands`
- `CliInteractiveToolCommands`
- `CliInteractiveSessionCommands`
- `CliInteractivePresenter`
- `CliInteractiveConversationRunner`

Compatibility wrappers remain:

- `ConfigStore`
- `SessionStore`

These keep existing tests and callers working while runtime code uses injected services.

### Desktop panel and workflow extraction

The desktop app has started moving large UI and workflow responsibilities out of `MainWindow`.

Already separated:

- Git panel UserControl and workflow service
- Verification panel UserControl and workflow services
- Settings panel UserControl
- Chat panel UserControl
- Plan panel UserControl
- Memory/session summary panel UserControl
- File change review panel UserControl
- Run timeline panel UserControl
- Auto-fix workflow service
- attachment selection workflow service
- clipboard service
- workspace analysis and project config services

### Provider model catalog extraction

Desktop provider/model metadata was moved out of `MainViewModel` and into `DesktopProviderModelCatalog`.

This keeps view state focused on UI behavior while preserving existing provider defaults.

### Git branch state guidance

The desktop Git panel now annotates raw `git status --short --branch` output with branch guidance for:

- missing upstream configuration
- deleted upstream branches
- ahead/behind/diverged local branches
- detached HEAD

The Git panel also supports the first write-side workflow:

- stage selected file
- stage approved files
- unstage selected file
- commit staged files with a typed commit message
- pull with `git pull --ff-only` when the working tree and branch state are safe

Pull is blocked when:

- local changes are present
- no upstream is configured
- upstream is deleted
- the branch has local-only commits
- the branch has diverged
- the repository is in detached HEAD

### First-run setup guidance

The CLI now offers to run `/setup` immediately when interactive startup is missing a model or API key. The Desktop first-run status/log text also points users to the Settings panel and Save action.

### Global tool smoke test

The local package/update flow was validated:

- `dotnet pack .\csharp\AgentQ.Cli\AgentQ.Cli.csproj -c Release`
- `dotnet tool update --global --add-source .\artifacts\packages AgentQ.Tool`
- `agentq --prompt "hello" --json`
- `dotnet run --project .\csharp\AgentQ.Cli -- --prompt "hello" --json`

The installed global tool updated to `1.0.260520.12824` and both installed/direct CLI execution succeeded with the saved `opencode-go` config.

## Current Verification Snapshot

Last verified on 2026-05-20:

- `dotnet test .\csharp\AgentQ.sln`: `101/101` passed

Note: `dotnet test --no-restore` can fail after a fresh branch sync if `obj\project.assets.json` does not yet contain the `net10.0-windows` target. Run a normal restore/test first.

## Active Work Queue

### 1. Continue desktop panel extraction

Priority: highest

Current status:

- Settings, Chat, Git, Verification, Plan, Memory, File Change Review, and Run Timeline panels are separated
- `MainWindow.xaml` and `MainWindow.xaml.cs` still own project UI

Target:

- extract the next focused panel without changing behavior
- extract project UI without changing behavior
- add or update tests only where service behavior changes

Primary files:

- `csharp/AgentQ.Desktop/MainWindow.xaml`
- `csharp/AgentQ.Desktop/MainWindow.xaml.cs`
- `csharp/AgentQ.Desktop/Views/*`
- `csharp/AgentQ.Desktop/ViewModels/MainViewModel.cs`

### 2. Improve desktop Git branch recovery actions

Priority: high

Current issue:

- real workflows include branch consolidation, deleted upstream branches, forced updates, and divergent local branches
- the desktop Git panel now explains those states and can stage/commit/pull safely, but branch recovery actions still need explicit flows

Target:

- consider backup-branch guidance before hard resets or branch switches
- keep destructive/reset-style actions blocked unless a separate safe recovery flow exists

Primary files:

- `csharp/AgentQ.Desktop/Views/GitPanel.xaml`
- `csharp/AgentQ.Desktop/Services/DesktopGitService.cs`
- `csharp/AgentQ.Desktop/Services/DesktopGitPanelWorkflowService.cs`
- `csharp/AgentQ.Tests/DesktopServiceTests.cs`

### 3. Continue REPL UX stabilization

Priority: medium

Target:

- make tool logs concise and predictable
- keep permission prompts clear
- reduce noisy redraw behavior
- add focused regression coverage where practical

Primary files:

- `csharp/AgentQ.Cli/CliInteractiveConversationRunner.cs`
- `csharp/AgentQ.Cli/CliInteractivePresenter.cs`
- `csharp/AgentQ.Cli/ConsolePermissionEnforcer.cs`

## Definition Of Done For This Hardening Pass

- planning docs and README match current implementation
- direct CLI and installed `agentq` behavior match after local update
- non-interactive modes work: `--prompt`, `--stdin`, `--input`, `--json`
- interactive responses remain visible
- tool arguments in object or string form do not crash permission flow
- config persistence is easy to discover and use
- provider/model metadata is not hardcoded inside desktop view state
- desktop panel extraction continues without breaking existing workflows
- build, unit tests, and integration tests pass

## Immediate Next Step

Extract the project panel, then revisit branch recovery UX.
