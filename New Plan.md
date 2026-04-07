# AgentQ New Plan

Updated: 2026-04-07

## Current Position

AgentQ is now on an automation-first track.

The base automation path already exists:

- `.NET global tool` packaging exists and `agentq` is the command name
- `--prompt`, `--stdin`, and `--input` all enter non-interactive one-shot execution
- `--json` returns a machine-readable envelope
- non-interactive exit codes are defined
- non-interactive permission policy exists
  - default deny
  - `--yes` for full allow
  - repeated `--allow-tool <name>` for narrow allow
- automation helper tests exist
- process-level automation integration tests exist

Current validation snapshot:

- `test.cmd`: `44` non-integration tests passed
- `test.integration.cmd`: `4` integration tests passed
- automation-related unit/integration additions are passing
- `dotnet pack .\csharp\AgentQ.Cli\AgentQ.Cli.csproj -c Release`: passed
- local tool install from `.\artifacts\packages`: passed

## JSON Contract Baseline

Current non-interactive `--json` output includes:

- `success`
- `exitCode`
- `terminationReason`
- `finalText`
- `allowedTools`
- `deniedTools`
- `toolErrors`
- `toolOutputs`
- `messageCount`

Current `terminationReason` values:

- `completed`
- `configuration_error`
- `invalid_arguments`
- `permission_denied`
- `tool_error`
- `provider_error`

## What Is Already Done

These should not stay in the active work queue:

- core CLI loop and provider abstraction
- tool execution and workspace safety
- session/config persistence
- conversation compaction
- wrapper build/test scripts
- `.NET global tool` packaging
- non-interactive entrypoint
- JSON output baseline
- exit-code baseline
- non-interactive permission baseline
- automation process-level integration coverage

## Remaining Work

### 1. Structure `toolOutputs`

Current issue:

- `toolOutputs` is still a string array
- automation clients must parse tool JSON again themselves

Target:

- each `toolOutputs` item should become a structured object
- preserve raw text when parsing fails
- expose whether the payload was valid JSON

Recommended shape:

- `toolName`
- `isError`
- `raw`
- `isJson`
- `parsed`

Primary files:

- `csharp/AgentQ.Cli/Program.cs`
- `csharp/AgentQ.Cli/AutomationSupport.cs`
- `csharp/AgentQ.Tests/AutomationSupportTests.cs`
- `csharp/AgentQ.Tests/AutomationCliIntegrationTests.cs`

### 2. Add explicit deny controls

Current issue:

- permission policy can allow all or allow named tools
- there is no higher-priority deny rule

Target:

- add `--deny-tool <name>`
- make deny override allow
- reflect the effective policy in JSON output

Primary files:

- `csharp/AgentQ.Core/Providers/ProviderFactory.cs`
- `csharp/AgentQ.Cli/NonInteractivePermissionEnforcer.cs`
- `csharp/AgentQ.Cli/AutomationSupport.cs`
- `csharp/AgentQ.Tests/AutomationSupportTests.cs`

### 3. Expand automation JSON metadata

Current issue:

- output is good enough for basic automation
- still thin for richer orchestration

Target:

- include effective permission policy summary
- include provider/model/base-url in JSON when useful
- optionally include token usage if available
- optionally include executed tool names separately from tool outputs

Primary files:

- `csharp/AgentQ.Cli/AutomationSupport.cs`
- `csharp/AgentQ.Cli/Program.cs`
- `csharp/AgentQ.Core/Models/ChatModels.cs` if needed

### 4. Packaged-tool smoke coverage

Current issue:

- process-level tests currently run against built `AgentQ.Cli.exe`
- packaged-tool install path was manually verified, not yet test-covered

Target:

- decide whether to add a lightweight smoke test for packed/global-tool behavior
- if too heavy for regular integration, document it as a release checklist item

Primary files:

- `csharp/AgentQ.Tests/AutomationCliIntegrationTests.cs`
- `README.md`

### 5. Final docs cleanup

Current issue:

- README is mostly aligned
- examples and contract notes can still be tightened

Target:

- document the final automation JSON contract
- document interactive mode vs automation mode cleanly
- document permission policy with examples
- document install/update/uninstall flow in one place

Primary files:

- `README.md`
- `New Plan.md`

## Recommended Order

1. Structure `toolOutputs`
2. Add explicit deny controls
3. Expand automation JSON metadata
4. Final docs cleanup
5. Optional packaged-tool smoke coverage

Reason:

- `toolOutputs` structure is the biggest remaining automation usability gap
- deny rules should land before calling the policy complete
- docs should reflect the final contract, not an intermediate one

## Definition Of Done

The current automation track is complete when:

- `agentq` still works interactively
- `agentq --prompt`, `--stdin`, and `--input` all work reliably
- non-interactive JSON output is stable and structured enough for scripting
- permission policy supports allow and deny behavior explicitly
- exit codes remain deterministic
- automation process-level tests cover the final contract
- README matches the actual CLI behavior

## Immediate Next Step

Implement structured `toolOutputs` in the non-interactive JSON envelope.

That is the next highest-value change because it improves downstream automation without changing the already-stable entrypoint contract.
