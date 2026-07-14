# Test stability and split plan

## Current boundary

`AgentQ.Tests` remains the integration test host until the Runtime/Core contracts are owned by the integration session. Process, port, and filesystem tests must use unique temporary roots and must clean only processes whose parent chain belongs to the active test host.

## Incremental extraction

1. Move local-server, language-worker, MCP stdio, browser preview, and verification-runner tests from `DesktopServiceTests.cs` into `DesktopProcessLifecycleTests.cs`.
2. Move scaffold and workspace path tests into `DesktopScaffoldingAndPathTests.cs`.
3. Move WPF/ViewModel tests into `DesktopPresentationTests.cs`; keep STA/Dispatcher tests in one explicit non-parallel collection.
4. Once the integration session creates the shared Runtime contracts, create separate Core, Runtime, Security, Scaffolding, Verification, Memory, Desktop presentation, and E2E projects. Do not change solution/project references from this workstream.

Each extraction must preserve fully equivalent assertions, run its focused group three times, then run the affected broader group. The original test file must lose the moved tests in the same change so coverage is not duplicated.

## CI quality gates

- Keep Cobertura as an uploaded artifact, then publish a summary and fail on a configured overall line-rate floor.
- Use Coverlet branch coverage filters for the safety-critical namespaces: intent routing, permission enforcement, workspace paths, scaffold authorization, completion guards, and process lifecycle.
- Run a changed-lines coverage gate on pull requests (for example `diff-cover` against the merge base) separately from the repository-wide floor. Start in report-only mode, collect a baseline, then enforce an agreed floor.
- Retain TRX, vstest diagnostics, hang/crash dumps, and process snapshots on every failed or timed-out run.

## Ownership invariant

Diagnostics record PID, parent PID, command line, and start time for every test-host descendant. Timeout cleanup may target only the known test root with a tree kill; unknown or re-parented processes are reported, never terminated by name.
