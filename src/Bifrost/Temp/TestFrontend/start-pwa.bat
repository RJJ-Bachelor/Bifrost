@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-pwa.ps1" %*
set "pwaExitCode=%ERRORLEVEL%"
pause
exit /b %pwaExitCode%
