@echo off
REM Launcher for OmenGamingHubUnlocker.ps1
REM Runs PowerShell with ExecutionPolicy Bypass for this process only.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0OmenGamingHubUnlocker.ps1"
exit /b
