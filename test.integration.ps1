param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$nugetConfig = Join-Path $repoRoot "NuGet.Config"
$testProject = Join-Path $repoRoot "csharp\AgentQ.Tests\AgentQ.Tests.csproj"
$testAssembly = Join-Path $repoRoot "csharp\AgentQ.Tests\bin\Debug\net10.0\AgentQ.Tests.dll"

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

if ($env:DOTNET_CLI_HOME) {
    Write-Host "DOTNET_CLI_HOME=$($env:DOTNET_CLI_HOME)"
}
else {
    Write-Host "DOTNET_CLI_HOME is not set; using default dotnet environment"
}
Write-Host "Building test assembly"

& dotnet msbuild $testProject /t:Build /p:BuildProjectReferences=false /p:RestoreConfigFile=$nugetConfig /m:1 /v:minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Running integration tests"
& dotnet vstest $testAssembly "--TestCaseFilter:Category=Integration"
exit $LASTEXITCODE
