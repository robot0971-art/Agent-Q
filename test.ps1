param(
    [string]$Filter = "Category!=Integration",
    [int]$HangTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$nugetConfig = Join-Path $repoRoot "NuGet.Config"
$testProject = Join-Path $repoRoot "csharp\AgentQ.Tests\AgentQ.Tests.csproj"
$testAssembly = Join-Path $repoRoot "csharp\AgentQ.Tests\bin\Debug\net10.0-windows\AgentQ.Tests.dll"

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

function Assert-DotNetAvailable {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error ".NET SDK was not found. Install the .NET 10 SDK and ensure 'dotnet' is available on PATH, then rerun this script."
        exit 1
    }

    $sdkVersions = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to query installed .NET SDKs with 'dotnet --list-sdks'."
        exit $LASTEXITCODE
    }

    if (-not ($sdkVersions | Where-Object { $_ -match '^10\.' })) {
        Write-Error ".NET 10 SDK was not found. Installed SDKs: $($sdkVersions -join ', '). Install the .NET 10 SDK, then rerun this script."
        exit 1
    }
}

Assert-DotNetAvailable

if ($env:DOTNET_CLI_HOME) {
    Write-Host "DOTNET_CLI_HOME=$($env:DOTNET_CLI_HOME)"
}
else {
    Write-Host "DOTNET_CLI_HOME is not set; using default dotnet environment"
}

Write-Host "Restoring test project"
& dotnet restore $testProject /p:RestoreConfigFile=$nugetConfig
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Building test assembly"

& dotnet msbuild $testProject /t:Build /p:BuildProjectReferences=false /p:RestoreConfigFile=$nugetConfig /m:1 /v:minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Running tests with filter: $Filter (hang diagnostics: $HangTimeoutSeconds seconds)"
$resultsDirectory = Join-Path $repoRoot "artifacts\test-results"
New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null
& dotnet test $testProject --no-build "--filter:$Filter" --blame-hang "--blame-hang-timeout:$($HangTimeoutSeconds)s" --blame-crash --diag (Join-Path $resultsDirectory "vstest-diag.log") --logger "trx;LogFileName=agentq-tests.trx" --results-directory $resultsDirectory
exit $LASTEXITCODE
