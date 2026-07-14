@echo off
setlocal

set "REPO_ROOT=%~dp0"
set "NUGET_CONFIG=%REPO_ROOT%NuGet.Config"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "TEST_DLL=%REPO_ROOT%csharp\AgentQ.Tests\bin\Debug\net10.0-windows\AgentQ.Tests.dll"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK was not found. Install the .NET 10 SDK and ensure dotnet is available on PATH, then rerun this script.
  exit /b 1
)

dotnet --list-sdks | findstr /r "^10\." >nul
if errorlevel 1 (
  echo .NET 10 SDK was not found. Install the .NET 10 SDK, then rerun this script.
  echo Installed SDKs:
  dotnet --list-sdks
  exit /b 1
)

if defined DOTNET_CLI_HOME (
  echo DOTNET_CLI_HOME=%DOTNET_CLI_HOME%
) else (
  echo DOTNET_CLI_HOME is not set; using default dotnet environment
)

echo Restoring test project
dotnet restore "%REPO_ROOT%csharp\AgentQ.Tests\AgentQ.Tests.csproj" /p:RestoreConfigFile="%NUGET_CONFIG%"
if errorlevel 1 exit /b %ERRORLEVEL%

echo Building test assembly
dotnet msbuild "%REPO_ROOT%csharp\AgentQ.Tests\AgentQ.Tests.csproj" /t:Build /p:BuildProjectReferences=false /p:RestoreConfigFile="%NUGET_CONFIG%" /m:1 /v:minimal
if errorlevel 1 exit /b %ERRORLEVEL%

echo Running non-integration tests with hang and crash diagnostics
if not exist "%REPO_ROOT%artifacts\test-results" mkdir "%REPO_ROOT%artifacts\test-results"
dotnet test "%REPO_ROOT%csharp\AgentQ.Tests\AgentQ.Tests.csproj" --no-build --filter "Category!=Integration" --blame-hang --blame-hang-timeout 60s --blame-crash --diag "%REPO_ROOT%artifacts\test-results\vstest-diag.log" --logger "trx;LogFileName=agentq-tests.trx" --results-directory "%REPO_ROOT%artifacts\test-results"
exit /b %ERRORLEVEL%
