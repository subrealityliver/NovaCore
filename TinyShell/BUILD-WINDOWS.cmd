@echo off
setlocal
cd /d "%~dp0"
echo Building TinyShell...
dotnet build TinyShell.sln -c Release -r win-x64
if errorlevel 1 (
  echo.
  echo BUILD FAILED.
  exit /b %errorlevel%
)
echo.
echo BUILD SUCCEEDED.
endlocal
