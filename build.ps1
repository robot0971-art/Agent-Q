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
    "csharp\AgentQ.Tests\AgentQ.Tests.csproj"
)

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

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
