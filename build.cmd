@echo off
setlocal

set "REPO_ROOT=%~dp0"
set "NUGET_CONFIG=%REPO_ROOT%NuGet.Config"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

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

echo Restoring AgentQ solution
dotnet restore "%REPO_ROOT%csharp\AgentQ.sln" /p:RestoreConfigFile="%NUGET_CONFIG%"
if errorlevel 1 exit /b %ERRORLEVEL%

echo Building AgentQ projects

call :build "%REPO_ROOT%csharp\AgentQ.Api\AgentQ.Api.csproj" %* || exit /b 1
call :build "%REPO_ROOT%csharp\AgentQ.Core\AgentQ.Core.csproj" %* || exit /b 1
call :build "%REPO_ROOT%csharp\AgentQ.Tools\AgentQ.Tools.csproj" %* || exit /b 1
call :build "%REPO_ROOT%csharp\AgentQ.Providers.Anthropic\AgentQ.Providers.Anthropic.csproj" %* || exit /b 1
call :build "%REPO_ROOT%csharp\AgentQ.Providers.OpenAi\AgentQ.Providers.OpenAi.csproj" %* || exit /b 1
call :build "%REPO_ROOT%csharp\AgentQ.Cli\AgentQ.Cli.csproj" %* || exit /b 1
call :build "%REPO_ROOT%csharp\AgentQ.MockService\AgentQ.MockService.csproj" %* || exit /b 1
call :build "%REPO_ROOT%csharp\AgentQ.Desktop\AgentQ.Desktop.csproj" %* || exit /b 1
call :build "%REPO_ROOT%csharp\AgentQ.Tests\AgentQ.Tests.csproj" %* || exit /b 1

exit /b 0

:build
echo.
echo [build] %~1
dotnet msbuild "%~1" /t:Build /p:BuildProjectReferences=false /p:RestoreConfigFile="%NUGET_CONFIG%" /m:1 /v:minimal %2 %3 %4 %5 %6 %7 %8 %9
exit /b %ERRORLEVEL%
