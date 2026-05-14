@echo off
setlocal
cd /d "%~dp0"

dotnet publish .\csharp\AgentQ.Desktop\AgentQ.Desktop.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o .\artifacts\desktop\win-x64

if errorlevel 1 exit /b %errorlevel%

echo.
echo Published AgentQ Desktop:
echo %CD%\artifacts\desktop\win-x64\AgentQ.Desktop.exe
