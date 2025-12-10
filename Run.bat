@echo off
setlocal

set SCRIPT_DIR=%~dp0

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%OmenGamingHubUnlocker.ps1"

echo.
echo Omen Gaming Hub Unlocker finished.
echo Press any key to close this window...
pause >nul

endlocal
