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
