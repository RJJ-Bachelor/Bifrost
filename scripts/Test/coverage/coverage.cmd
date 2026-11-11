@echo off
setlocal
for %%i in ("%~dp0..\..\..") do set "REPO_ROOT=%%~fi"
if /i "%~1"=="--coverage-locked" goto coverage
set "BIFROST_COVERAGE_SCRIPT=%~f0"
set "BIFROST_COVERAGE_ROOT=%REPO_ROOT%"
powershell.exe -NoProfile -Command "$ErrorActionPreference = 'Stop'; $sha = [System.Security.Cryptography.SHA256]::Create(); try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($env:BIFROST_COVERAGE_ROOT.ToUpperInvariant()))).Replace('-', '') } finally { $sha.Dispose() }; $mutex = [System.Threading.Mutex]::new($false, ('Local\BifrostCoverage-' + $hash)); $locked = $false; try { try { $locked = $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $locked = $true }; if (-not $locked) { Write-Host 'Coverage koerer allerede for dette repo. Vent til den er faerdig, og proev igen.'; exit 1 }; & $env:BIFROST_COVERAGE_SCRIPT '--coverage-locked'; exit $LASTEXITCODE } finally { if ($locked) { $mutex.ReleaseMutex() }; $mutex.Dispose() }"
set "COVERAGE_EXIT_CODE=%ERRORLEVEL%"
pause
exit /b %COVERAGE_EXIT_CODE%

:coverage
set "COVERAGE_DIR=%REPO_ROOT%\Test\coverage"
set "TEST_RESULTS_DIR=%COVERAGE_DIR%\TestResults"
set "TEST_LOG=%COVERAGE_DIR%\coverage.log"
set "COVERAGE_SETTINGS=%~dp0coverage.runsettings"
cd /d "%REPO_ROOT%\src\Bifrost"
if errorlevel 1 goto fejl

echo [1/4] Sletter gamle resultater...
if exist "%COVERAGE_DIR%" rd /s /q "%COVERAGE_DIR%"
if exist "%COVERAGE_DIR%" goto fejl
mkdir "%COVERAGE_DIR%"
if errorlevel 1 goto fejl

echo [2/4] Tjekker ReportGenerator...
set "PATH=%PATH%;%USERPROFILE%\.dotnet\tools"
where reportgenerator >nul 2>nul
if not errorlevel 1 goto test
dotnet tool install -g dotnet-reportgenerator-globaltool
if errorlevel 1 goto fejl

:test
echo [3/4] Koerer tests med coverage...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0backend-tests.ps1" -ResultsDirectory "%TEST_RESULTS_DIR%" -LogPath "%TEST_LOG%"
set "TEST_EXIT_CODE=%ERRORLEVEL%"
if not "%TEST_EXIT_CODE%"=="0" echo ADVARSEL: Build eller test fejlede. Rapporten laves med de tilgaengelige resultater.

echo [4/4] Laver rapport...
reportgenerator -reports:"%TEST_RESULTS_DIR%\**\coverage.cobertura.xml" -targetdir:"%COVERAGE_DIR%" -reporttypes:"Html;JsonSummary;TextSummary;Cobertura" -assemblyfilters:"-*.Tests;-*.Test;-*.IntegrationTests;-MimirService.*;-YggdrasilService.*"
if errorlevel 1 goto fejl
reportgenerator -reports:"%TEST_RESULTS_DIR%\**\coverage.cobertura.xml" -targetdir:"%COVERAGE_DIR%\without-migrations" -reporttypes:"Html;JsonSummary;TextSummary" -assemblyfilters:"-*.Tests;-*.Test;-*.IntegrationTests;-MimirService.*;-YggdrasilService.*" -filefilters:"-**/Migrations/**"
if errorlevel 1 goto fejl

start "" "%COVERAGE_DIR%\index.html"
echo.
echo Rapporten er gemt i "%COVERAGE_DIR%\index.html".
if not "%TEST_EXIT_CODE%"=="0" echo Build eller test fejlede. Se fejlene i "%TEST_LOG%".
exit /b %TEST_EXIT_CODE%

:fejl
echo.
echo Noget gik galt. Tjek meddelelserne ovenfor.
if exist "%TEST_LOG%" echo Buildloggen findes i "%TEST_LOG%".
exit /b 1
