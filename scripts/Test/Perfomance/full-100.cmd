@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0performance.ps1" -Mode Full -ConfigPath "%~dp0..\..\..\src\Bifrost\Backend\Backend.Performance.Tests\Configuration\full-100.json" %*
set "PERFORMANCE_EXIT_CODE=%ERRORLEVEL%"
if not "%BIFROST_PERF_NO_PAUSE%"=="1" pause
exit /b %PERFORMANCE_EXIT_CODE%
