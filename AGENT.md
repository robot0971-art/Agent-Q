# AgentQ

## Overview

- Name: `AgentQ`
- Language: `C#`
- Runtime: `.NET 10`
- Goal: build a tool-using coding assistant CLI with swappable LLM providers

AgentQ is no longer just a design-stage migration plan. The repository already contains a working CLI loop, multi-provider abstraction, tool execution pipeline, mock service, and a meaningful test base. The project is currently in the `stabilize and verify` phase.

## Current Architecture

```text
AgentQ.sln
|- AgentQ.Api                  Shared DTOs and API contracts
|- AgentQ.Core                 Common chat models and provider abstractions
|- AgentQ.Providers.Anthropic  Anthropic provider
|- AgentQ.Providers.OpenAi     OpenAI-compatible provider
|- AgentQ.Tools                Tool implementations and path safety
|- AgentQ.Cli                  REPL, slash commands, session/config handling
|- AgentQ.MockService          Mock Anthropic-style service for parity tests
`- AgentQ.Tests                Unit and integration tests
```

## What Is Already Implemented

### CLI

- interactive REPL
- tool-use conversation loop
- provider switching
- model switching
- base URL switching
- session save/load
- config persistence
- permission-gated tool execution

### Providers

- `ILlmProvider`
- `ProviderFactory`
- `ResilientLlmProvider`
- `AnthropicProvider`
- `OpenAiCompatibleProvider`

### Tools

- `bash`
- `read_file`
- `write_file`
- `edit_file`
- `grep`
- `glob`
- `plugin_echo`

### Safety and Reliability

- workspace-root restriction via `ToolPathGuard`
- permission prompts for gated tools
- `bash` timeout bounds
- basic dangerous command blocking
- stdout/stderr truncation
- `maxSteps` to stop runaway tool loops
- shared streamed tool-call delta buffering
- recursive JSON parsing for nested tool inputs

## Current CLI Commands

- `/help`
- `/status`
- `/clear`
- `/run`
- `/provider`
- `/model`
- `/base-url`
- `/timeout`
- `/config save`
- `/save`
- `/load`
- `/exit`

## Verification Strategy

The repository now contains standard local verification entrypoints:

- `build.cmd`
- `test.cmd`
- `build.ps1`
- `test.ps1`

These scripts are the intended default build/test entrypoints and use the current local dotnet environment by default.

## Known Environment Limits

The current sandbox is not a normal local development environment.

- regular `dotnet build` / `dotnet test` can be unstable here
- `HttpListener`-based integration tests can fail in this runtime
- external NuGet access can fail with `NU1301`

Because of that, some validation in this environment has required:

- direct `dotnet msbuild`
- direct `dotnet vstest`
- separation of integration tests from normal test runs

This is an environment limitation, not a confirmed source-level failure.

## Current Focus

The project no longer needs major architectural invention. The main remaining work is:

1. documentation synchronization and final cleanup
2. optional provider integration expansion beyond current local coverage
3. optional CLI rendering and UX polish

## Current Validation Snapshot

- full local test suite passed: `28`
- `CliToolLoopRunnerTests`: `11` passed
- `ToolAndConfigurationTests`: `25` passed
- `ProviderUnitTests`: `6` passed
- integration tests passed locally: `4`

## Notes

- earlier planning language that described multi-provider support as future work is now outdated
- the codebase already targets `net10.0`, not `.NET 8`
- Anthropic and OpenAI-compatible providers are already implemented
