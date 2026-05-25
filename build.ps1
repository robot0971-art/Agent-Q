param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$MsbuildArgs
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$nugetConfig = Join-Path $repoRoot "NuGet.Config"
$projects = @(
    "csharp\AgentQ.Api\AgentQ.Api.csproj",
    "csharp\AgentQ.Core\AgentQ.Core.csproj",
    "csharp\AgentQ.Tools\AgentQ.Tools.csproj",
    "csharp\AgentQ.Providers.Anthropic\AgentQ.Providers.Anthropic.csproj",
    "csharp\AgentQ.Providers.OpenAi\AgentQ.Providers.OpenAi.csproj",
    "csharp\AgentQ.Cli\AgentQ.Cli.csproj",
    "csharp\AgentQ.MockService\AgentQ.MockService.csproj",
    "csharp\AgentQ.Desktop\AgentQ.Desktop.csproj",
    "csharp\AgentQ.Tests\AgentQ.Tests.csproj"
)

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

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host ""
    Write-Host "[build] $projectPath"
    & dotnet msbuild $projectPath /t:Build /p:BuildProjectReferences=false /p:RestoreConfigFile=$nugetConfig /m:1 /v:minimal @MsbuildArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

exit 0
