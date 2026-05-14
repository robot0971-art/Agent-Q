@echo off
setlocal
cd /d "%~dp0"

dotnet build .\csharp\AgentQ.Desktop\AgentQ.Desktop.csproj -c Debug -m:1
if errorlevel 1 exit /b %errorlevel%

echo.
echo Desktop build complete.
echo Run with:
echo dotnet run --project .\csharp\AgentQ.Desktop\AgentQ.Desktop.csproj
