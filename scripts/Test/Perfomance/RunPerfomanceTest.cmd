@echo off
setlocal

set "BIFROST_PERF_NO_PAUSE=1"
set "PERFORMANCE_SCRIPT_DIR=%~dp0"

echo [0/5] Stopper et eventuelt gammelt Aspire-miljo...
call "%PERFORMANCE_SCRIPT_DIR%stop-aspire.cmd"
if errorlevel 1 echo Ingen eksisterende Aspire-session blev stoppet; fortsaetter.

echo [1/5] Klargoring...
call "%PERFORMANCE_SCRIPT_DIR%setup.cmd"
if errorlevel 1 goto failed

echo [2/5] Starter Aspire...
call "%PERFORMANCE_SCRIPT_DIR%start-aspire.cmd"
if errorlevel 1 goto failed

echo [3/4] Kontrollerer gateway...
call "%PERFORMANCE_SCRIPT_DIR%check.cmd"
if errorlevel 1 goto failed

echo [4/4] Korer normal full performance-test og abner rapporten...
call "%PERFORMANCE_SCRIPT_DIR%full.cmd" -OpenReport
set "PERFORMANCE_EXIT_CODE=%ERRORLEVEL%"
if errorlevel 1 goto cleanup

echo [6/6] Korer 100%% worst-case performance-test og abner rapporten...
call "%PERFORMANCE_SCRIPT_DIR%full-100.cmd" -OpenReport
set "PERFORMANCE_EXIT_CODE=%ERRORLEVEL%"
goto cleanup

:failed
set "PERFORMANCE_EXIT_CODE=%ERRORLEVEL%"
if "%PERFORMANCE_EXIT_CODE%"=="0" set "PERFORMANCE_EXIT_CODE=1"

:cleanup
echo.
echo Stopper Aspire, databaser, RabbitMQ og backendservices...
call "%PERFORMANCE_SCRIPT_DIR%stop-aspire.cmd"
if errorlevel 1 echo Advarsel: Aspire kunne ikke stoppes automatisk.

if not "%PERFORMANCE_EXIT_CODE%"=="0" goto failed_message
echo Performance-test gennemfort, og miljoet er stoppet.
pause
exit /b 0

:failed_message
echo.
echo Performance-test stoppede ved en fejl (exitkode %PERFORMANCE_EXIT_CODE%).
pause
exit /b %PERFORMANCE_EXIT_CODE%
