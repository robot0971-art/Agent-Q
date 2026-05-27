param(
    [switch]$SkipGitStatus
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $repoRoot "csharp\AgentQ.sln"

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:CI = "true"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$Name failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

function Assert-DotNetAvailable {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error ".NET SDK was not found. Install the .NET 10 SDK and ensure 'dotnet' is available on PATH."
        exit 1
    }

    $sdkVersions = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to query installed .NET SDKs with 'dotnet --list-sdks'."
        exit $LASTEXITCODE
    }

    if (-not ($sdkVersions | Where-Object { $_ -match '^10\.' })) {
        Write-Error ".NET 10 SDK was not found. Installed SDKs: $($sdkVersions -join ', ')"
        exit 1
    }
}

Push-Location $repoRoot
try {
    Assert-DotNetAvailable

    Invoke-Step "Format check" {
        & dotnet format $solution --verify-no-changes --no-restore
    }

    Invoke-Step "Release rebuild" {
        & dotnet build $solution -c Release --no-restore /t:Rebuild
    }

    Invoke-Step "Debug wrapper build" {
        & (Join-Path $repoRoot "build.ps1")
    }

    Invoke-Step "Non-integration tests" {
        & (Join-Path $repoRoot "test.ps1")
    }

    if (-not $SkipGitStatus) {
        Invoke-Step "Git cleanliness check" {
            $status = & git status --short --branch
            if ($LASTEXITCODE -ne 0) {
                exit $LASTEXITCODE
            }

            $status | ForEach-Object { Write-Host $_ }
            $dirtyLines = @($status | Select-Object -Skip 1 | Where-Object { $_.Trim().Length -gt 0 })
            if ($dirtyLines.Count -gt 0) {
                Write-Error "Working tree is not clean. Commit, stash, or intentionally rerun with -SkipGitStatus before release tagging."
                exit 1
            }
        }
    }

    Write-Host ""
    Write-Host "Release readiness preflight passed."
    Write-Host ""
    Write-Host "Manual checks still required before publishing a beta:"
    Write-Host "- Install the draft installer on a clean Windows machine or VM."
    Write-Host "- Verify the portable ZIP from a path with spaces."
    Write-Host "- Run the CLI package smoke test from the downloaded .nupkg."
    Write-Host "- Attach an image/video through the Desktop file picker and confirm Evidence/Plan show it."
    Write-Host "- Exercise approve/reject/revert, snapshot rollback, memory operations, and telemetry/replay refresh."
}
finally {
    Pop-Location
}
