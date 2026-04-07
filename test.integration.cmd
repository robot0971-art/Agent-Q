@echo off
setlocal

set "REPO_ROOT=%~dp0"
set "NUGET_CONFIG=%REPO_ROOT%NuGet.Config"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "TEST_DLL=%REPO_ROOT%csharp\AgentQ.Tests\bin\Debug\net10.0\AgentQ.Tests.dll"

if defined DOTNET_CLI_HOME (
  echo DOTNET_CLI_HOME=%DOTNET_CLI_HOME%
) else (
  echo DOTNET_CLI_HOME is not set; using default dotnet environment
)
echo Building test assembly
dotnet msbuild "%REPO_ROOT%csharp\AgentQ.Tests\AgentQ.Tests.csproj" /t:Build /p:BuildProjectReferences=false /p:RestoreConfigFile="%NUGET_CONFIG%" /m:1 /v:minimal
if errorlevel 1 exit /b %ERRORLEVEL%

echo Running integration tests
dotnet vstest "%TEST_DLL%" --TestCaseFilter:"Category=Integration"
exit /b %ERRORLEVEL%
