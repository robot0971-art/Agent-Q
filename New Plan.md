# AgentQ Current Plan

Updated: 2026-05-19

## Current Position

AgentQ is past the prototype stage. The CLI, providers, tool execution loop, session/config persistence, non-interactive automation mode, tests, and desktop shell all exist.

Current work is focused on product hardening:

- keep the CLI startup and execution flow testable through dependency injection
- make interactive and non-interactive execution easier to maintain
- stabilize first-run configuration and installed-tool validation
- keep documentation aligned with actual behavior

## Recently Completed

### Tool step limit raised

The default tool-loop cap was raised from `8` to `45` in:

- `csharp/AgentQ.Cli/CliToolLoopRunner.cs`
- `csharp/AgentQ.Core/Models/ChatModels.cs`
- `csharp/AgentQ.Desktop/Services/DesktopAgentService.cs`

This reduces premature stops during broad codebase tasks while keeping an upper bound against runaway tool loops.

### CLI dependency injection refactor

The CLI now uses DI beyond the startup shell. `CliApplication` is no longer responsible for most storage, output, non-interactive execution, interactive command handling, presentation, or conversation rendering.

New runtime services:

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

### DI registration coverage

`ToolAndConfigurationTests` now verifies that `AddAgentQCli` can resolve the main runtime services and `CliApplication`.

### CLI presentation cleanup

The interactive startup display was moved into `CliInteractivePresenter`, and the broken encoded Q mark was replaced with stable ASCII presentation.

## Current Verification Snapshot

Last verified on 2026-05-19:

- `build.ps1` passed
- `test.ps1` passed: `71/71`
- `test.integration.ps1` passed: `14/14`

## Active Work Queue

### 1. Review and commit the DI refactor

Priority: highest

Target:

- inspect the full diff for accidental encoding churn or misplaced responsibilities
- keep unrelated generated files out of the commit, especially the untracked `nul` file
- commit the DI refactor as one coherent change

Primary files:

- `csharp/AgentQ.Cli/*`
- `csharp/AgentQ.Tests/ToolAndConfigurationTests.cs`
- `csharp/AgentQ.Tests/AgentQ.Tests.csproj`

### 2. Improve first-run configuration UX

Priority: high

Current issue:

- users still need to understand shell-local environment variables versus saved config
- missing model/API key guidance is helpful but not yet a smooth first-run path

Target:

- make `/setup` and `/config save` the obvious recommended path
- document CMD and PowerShell behavior clearly
- consider making interactive missing-config flow offer setup immediately

Primary files:

- `csharp/AgentQ.Cli/CliApplication.cs`
- `csharp/AgentQ.Cli/CliInteractivePersistenceCommands.cs`
- `csharp/AgentQ.Cli/CliInteractiveSettingsCommands.cs`
- `README.md`

### 3. Validate package/global-tool update flow

Priority: high

Current status:

- timestamped package versions are already implemented
- README documents `dotnet pack` and `dotnet tool update`

Target:

- run the full local package/update smoke test
- confirm installed `agentq` matches direct project execution
- document any remaining caveats

Recommended commands:

```cmd
dotnet pack .\csharp\AgentQ.Cli\AgentQ.Cli.csproj -c Release
dotnet tool update --global --add-source .\artifacts\packages AgentQ.Tool
agentq --prompt "hello" --json
```

### 4. Continue REPL UX stabilization

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

### 5. Resume provider compatibility validation

Priority: medium

Target:

- validate OpenAI-compatible providers against real endpoints
- keep Anthropic/OpenAI/OpenCode-Go behavior aligned
- expand provider tests only where compatibility issues are observed

## Definition Of Done For This Hardening Pass

- direct CLI and installed `agentq` behavior match after local update
- non-interactive modes work: `--prompt`, `--stdin`, `--input`, `--json`
- interactive responses remain visible
- tool arguments in object or string form do not crash permission flow
- config persistence is easy to discover and use
- DI service graph resolves in tests
- build, unit tests, and integration tests pass
- README and planning docs match the current implementation

## Immediate Next Step

Review the DI refactor diff, remove the stray untracked `nul` file if it is not needed, then prepare a focused commit.
