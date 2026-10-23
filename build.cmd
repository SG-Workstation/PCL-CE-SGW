@echo off
rem ===========================================================================
rem  PCL-CE build entry (double-click or run from cmd)
rem  All arguments are forwarded to build.ps1, e.g.:
rem      build.cmd
rem      build.cmd -Configuration Beta -Zip
rem      build.cmd -Clean -Configuration Debug -Tests
rem  Show all parameters (in PowerShell): Get-Help .\build.ps1 -Detailed
rem ===========================================================================
setlocal
chcp 65001 >nul

set "PS=pwsh"
where pwsh >nul 2>nul || set "PS=powershell"

"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
set "CODE=%ERRORLEVEL%"

if not "%CODE%"=="0" echo Build failed, exit code %CODE%

rem keep the window open when launched by double-click
echo %CMDCMDLINE% | find /i "%~nx0" >nul
if not errorlevel 1 pause

exit /b %CODE%
