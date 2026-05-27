# AgentQ Release Readiness Checklist

Updated: 2026-05-27

This checklist is for the current Windows desktop beta. Keep it focused on the release path that is already supported: Windows WPF desktop, CLI package, GitHub Releases, portable ZIP, and Inno Setup installer.

## Current Desktop Workflow

The release candidate should support this main path before publishing:

1. Open AgentQ Desktop.
2. Select or pass a workspace.
3. Run project analysis and confirm the Project panel identifies the project type, key files, commands, symbols, and memory.
4. Ask AgentQ to inspect, explain, or modify a focused area.
5. Confirm Evidence records why files, searches, commands, visual attachments, and verification were used.
6. Review any file changes in Change preview.
7. Run focused verification from the Verify flow or through a successful shell command that creates a Verify card.
8. Review Git status/diff.
9. Commit only after the changed files and verification result are understood.

## Known Limitations

- The Windows desktop executable and installer are not code-signed yet. SmartScreen or browser warnings are expected for beta builds.
- The desktop app is still Windows-focused through WPF and `net10.0-windows`.
- Linux/macOS desktop support requires a future Avalonia or equivalent UI migration.
- MCP configuration exists, but long-running persistent MCP sessions are still a later enhancement.
- Visual evidence is covered by automated image/Plan evidence tests, but a manual file-picker smoke test should still be run before a public beta.
- Unity verification may require manual Unity Editor or batchmode checks unless the target project exposes command-line tests.
- Embeddings are optional and require a configured embedding provider plus a built local index.
- Video attachment analysis depends on `ffmpeg` being available in `PATH` for frame extraction.
- The current beta is intended for trusted users who can review changes before commit.

## Pre-Release Validation

Run these locally before creating or publishing a release tag:

```powershell
.\release-readiness.ps1
```

If you are intentionally running the preflight while local documentation or plan updates are still uncommitted, use:

```powershell
.\release-readiness.ps1 -SkipGitStatus
```

Confirm:

- Build succeeds.
- Non-integration tests pass.
- Format check has no changes.
- `git status --short --branch` is clean.
- `New Plan.md` has no completed item still listed in the active queue.

## Release Artifact QA

After the tag workflow creates a draft release, download artifacts from the draft and verify them on a clean Windows machine or VM.

Expected artifacts:

- `AgentQ-Setup-<tag>.exe`
- `AgentQ.Desktop-win-x64-<tag>.zip`
- `AgentQ.Tool.<version>.nupkg`
- matching `.sha256` checksum files

Installer QA:

- Check SHA256 checksum against the published `.sha256` file.
- Run `AgentQ-Setup-<tag>.exe`.
- Confirm expected SmartScreen/unknown-publisher warning appears because the build is unsigned.
- Confirm install location is under `%LOCALAPPDATA%\Programs\AgentQ`.
- Confirm Start Menu shortcut opens AgentQ Desktop.
- Confirm optional desktop shortcut works if selected.
- Open Settings, configure a provider, save, close, and reopen.
- Confirm a simple prompt can run against a mock or real provider.
- Confirm uninstall removes the app entry.

Portable ZIP QA:

- Check SHA256 checksum against the published `.sha256` file.
- Extract `AgentQ.Desktop-win-x64-<tag>.zip` to a path with spaces.
- Run `AgentQ.Desktop.exe`.
- Confirm Settings can be saved.
- Confirm workspace analysis works on a small C# or TypeScript fixture.
- Confirm Git status/diff panel works in a disposable repository.

CLI Package QA:

- Install or update the package from the downloaded `.nupkg`.
- Run `agentq --prompt "hello" --json` with a mock or configured provider.
- Confirm JSON output reports success.

## Desktop Smoke Scenarios

Run these before publishing a beta release:

- C# bug fix with shell verification card.
- React/TypeScript feature change with frontend build or lint verification.
- Unity project analysis with visual evidence attachment.
- File change review with approve/reject/revert.
- Snapshot and rollback check on a disposable repository.
- Memory save/disable/delete check.
- Telemetry/replay dashboard refresh.

## Release Notes Draft

Use this short draft for the next beta release and adjust the tag/version before publishing:

```markdown
## AgentQ <tag> Beta

This beta focuses on desktop stabilization and repeatable coding-agent workflows.

Highlights:

- Project Map and multi-language workspace analysis for C#, TypeScript/JavaScript, Python, Docker, and Unity-style projects.
- Evidence, confidence, Verify cards, Run Summary, Plan evidence, Git review, snapshots, telemetry, and replay dashboard.
- Shell-run verification cards for successful `dotnet test`, localized test output, and frontend build output.
- Visual evidence handling for image/video attachments, with Plan evidence retaining attached visual context.
- Hardened provider secret storage and safer MCP server registry rules.
- CI/release improvements including format checks, warnings-as-errors in CI, coverage artifacts, installer/portable ZIP artifacts, and SHA256 checksums.

Known limitations:

- Windows desktop builds are currently unsigned and may trigger SmartScreen or browser warnings.
- Desktop UI is Windows-only for now.
- Persistent MCP sessions and broader cross-platform UI support are planned later.
```

## Code Signing Decision

Do not block the current internal beta on code signing. Keep the README and release notes explicit that the build is unsigned.

Before broader distribution, choose one of:

- EV/OV certificate with local or cloud-backed signing.
- Azure Key Vault or another HSM-backed signing service.
- CI signing step using a tool such as AzureSignTool after the executable and installer are built.

Signing should cover both:

- `AgentQ.Desktop.exe`
- `AgentQ-Setup-<tag>.exe`
