@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0stop-aspire.ps1" %*
set "STOP_EXIT_CODE=%ERRORLEVEL%"
if not "%BIFROST_PERF_NO_PAUSE%"=="1" pause
exit /b %STOP_EXIT_CODE%
