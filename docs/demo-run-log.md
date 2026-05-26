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
- Desktop UI verification for Project dashboard, Evidence, Change preview, Verify, Plan, and Git tabs remains to be performed interactively in AgentQ Desktop.
