@echo off
cd /d %~dp0
taskkill /F /IM WunderLanguage.Host.exe 2>nul
dotnet build
if errorlevel 1 (
    echo.
    echo BUILD FAILED - see errors above.
    pause
)