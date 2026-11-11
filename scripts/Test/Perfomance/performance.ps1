[CmdletBinding()]
param(
    [ValidateSet('Setup', 'Check', 'TeacherCheck', 'SelfTest', 'Smoke', 'BrowserSmoke', 'Full')]
    [string]$Mode = 'Smoke',
    [string]$GatewayUrl,
    [string]$ConfigPath,
    [switch]$NoBuild,
    [switch]$SkipBrowserInstall,
    [switch]$OpenReport,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$projectDirectory = Join-Path $repoRoot 'src/Bifrost/Backend/Backend.Performance.Tests'
$projectPath = Join-Path $projectDirectory 'Backend.Performance.Tests.csproj'
$outputPath = Join-Path $projectDirectory 'bin/Release/net10.0'
$assemblyPath = Join-Path $outputPath 'Backend.Performance.Tests.dll'
$originalEnvironment = @{}
$locationChanged = $false
$script:performanceExitCode = 0

function Set-TemporaryEnvironment {
    param([string]$Name, [string]$Value)
    if (-not $originalEnvironment.ContainsKey($Name)) {
        $originalEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name, 'Process')
    }
    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}

function Invoke-PerformanceCommand {
    param([string]$Command, [string[]]$Arguments, [switch]$AllowFailure)
    $previousPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 can wrap native stderr even when the command succeeds.
        $ErrorActionPreference = 'Continue'
        & $Command @Arguments
        $commandExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($commandExitCode -ne 0) {
        $script:performanceExitCode = $commandExitCode
        if (-not $AllowFailure) { throw "$Command afsluttede med exitkode $commandExitCode." }
    }
}

function Set-TeacherEnvironment {
    param([bool]$HasTokens, [uri]$Gateway)
    if ($HasTokens) { return }

    $isLocalGateway = $Gateway.IsLoopback -and $Gateway.Scheme -in @('http', 'https')
    foreach ($number in 1..30) {
        $usernameVariable = "BIFROST_PERF_TEACHER_${number}_USERNAME"
        $passwordVariable = "BIFROST_PERF_TEACHER_${number}_PASSWORD"
        $defaultUsername = "performance-teacher-$number"
        $username = [Environment]::GetEnvironmentVariable($usernameVariable)
        if ([string]::IsNullOrWhiteSpace($username)) {
            $username = $defaultUsername
            Set-TemporaryEnvironment -Name $usernameVariable -Value $username
        }
        if (-not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($passwordVariable))) { continue }
        if ($isLocalGateway -and $username -eq $defaultUsername) {
            # Matches VarService's local Development seed; explicit environment values take precedence.
            Set-TemporaryEnvironment -Name $passwordVariable -Value 'Pass123$'
            continue
        }
        $securePassword = Read-Host "Password til $username ($passwordVariable)" -AsSecureString
        try {
            $plainPassword = [System.Net.NetworkCredential]::new('', $securePassword).Password
            if ([string]::IsNullOrEmpty($plainPassword)) { throw 'Password maa ikke vaere tomt.' }
            Set-TemporaryEnvironment -Name $passwordVariable -Value $plainPassword
        } finally {
            $plainPassword = $null
            $securePassword.Dispose()
        }
    }
}

try {
    if ($ConfigPath -and $Mode -ne 'Full') {
        throw 'Brug -Mode Full sammen med -ConfigPath. Smoke og BrowserSmoke har deres egne profiler.'
    }
    # Resolve user paths against the caller's directory before changing location.
    $configurationPath = $null
    if ($ConfigPath) {
        $configurationPath = (Resolve-Path -LiteralPath $ConfigPath).Path
    } elseif ($Mode -eq 'Smoke') {
        $configurationPath = Join-Path $projectDirectory 'Configuration/smoke.json'
    } elseif ($Mode -eq 'BrowserSmoke') {
        $configurationPath = Join-Path $projectDirectory 'Configuration/smoke-browser.json'
    }
    $configuration = if ($configurationPath) { Get-Content -LiteralPath $configurationPath -Raw -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{} }
    $tokenVariables = @('BIFROST_PERF_TEACHER_1_JWT', 'BIFROST_PERF_TEACHER_2_JWT', 'BIFROST_PERF_TEACHER_3_JWT')
    if ($configuration.PSObject.Properties['TeacherTokenEnvironmentVariables']) {
        $tokenVariables = @($configuration.TeacherTokenEnvironmentVariables)
        if ($tokenVariables.Count -ne 30) { throw 'Konfigurationen skal angive tredive JWT-miljoevariabler.' }
    }
    $hasTokens = @($tokenVariables | Where-Object { -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }).Count -eq 30
    $isLoadTest = $Mode -in @('Smoke', 'BrowserSmoke', 'Full')
    $needsTeachers = $isLoadTest -or $Mode -eq 'TeacherCheck'
    $hasBrowserClients = $configuration.PSObject.Properties['BrowserClients'] -and $configuration.BrowserClients -gt 0
    $needsChromium = $Mode -eq 'Setup' -or ($needsTeachers -and (-not $hasTokens -or $hasBrowserClients))

    $applicationArguments = switch ($Mode) {
        'Setup' { @('--self-test') }
        'SelfTest' { @('--self-test') }
        'Check' { @('--check') }
        'TeacherCheck' { @('--check-teachers') }
        'Smoke' { @('--smoke') }
        'BrowserSmoke' { @('--config', $configurationPath) }
        'Full' { if ($configurationPath) { @('--config', $configurationPath) } }
    }
    $effectiveGateway = if ($GatewayUrl) { $GatewayUrl } elseif ($env:BIFROST_PERF_GATEWAY_URL) { $env:BIFROST_PERF_GATEWAY_URL } elseif ($configuration.PSObject.Properties['GatewayUrl']) { $configuration.GatewayUrl } else { 'http://localhost:6004' }
    if ($needsTeachers -or $Mode -eq 'Check') {
        Write-Host "Gateway: $effectiveGateway"
        Write-Host 'Aspire skal allerede koere; scriptsene starter ikke miljoeet.'
    }
    Write-Host "Profil: $Mode"
    if ($configurationPath) { Write-Host "Konfiguration: $configurationPath" }
    if ($Mode -eq 'Full' -and -not $ConfigPath) {
        Write-Host 'Fuld profil: 1.285 studerende (129 aktive = 10%), 10 sessions, 30 undervisere (3 pr. session), ca. 17 minutter plus setup/oprydning.'
    }
    if ($Mode -eq 'Full' -and $ConfigPath -and $configurationPath -like '*full-100.json') {
        Write-Host '100%-profil: 1.285 studerende, 1 HelpRequest pr. studerende (1.285 i alt), 10 sessions, 30 undervisere (3 pr. session).'
    }
    if ($DryRun) {
        Write-Host "Repository: $repoRoot"
        if (-not $NoBuild) { Write-Host "dotnet build `"$projectPath`" -c Release --nologo" }
        if ($needsChromium -and -not $SkipBrowserInstall) { Write-Host "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$outputPath/playwright.ps1`" install chromium" }
        Write-Host "dotnet `"$assemblyPath`" $($applicationArguments -join ' ')"
        Write-Host 'DryRun: ingen build, installation, passwordprompt eller netvaerkskald.'
    } else {
        Get-Command dotnet -ErrorAction Stop | Out-Null
        Push-Location -LiteralPath $repoRoot
        $locationChanged = $true
        if ($GatewayUrl) { Set-TemporaryEnvironment -Name 'BIFROST_PERF_GATEWAY_URL' -Value $GatewayUrl }
        if (-not $NoBuild) {
            Invoke-PerformanceCommand -Command 'dotnet' -Arguments @('build', $projectPath, '-c', 'Release', '--nologo')
        }
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw 'Release-build mangler. Koer setup.cmd eller udelad -NoBuild.'
        }
        if ($needsChromium -and -not $SkipBrowserInstall) {
            $playwrightScript = Join-Path $outputPath 'playwright.ps1'
            Invoke-PerformanceCommand -Command 'powershell.exe' -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $playwrightScript, 'install', 'chromium')
        }
        if ($needsTeachers) { Set-TeacherEnvironment -HasTokens $hasTokens -Gateway $effectiveGateway }

        $startedAt = [DateTime]::UtcNow
        Invoke-PerformanceCommand -Command 'dotnet' -Arguments (@($assemblyPath) + @($applicationArguments)) -AllowFailure

        if ($needsTeachers -or $Mode -eq 'Check') {
            $reportRoot = Join-Path $outputPath 'reports'
            if ($configuration.PSObject.Properties['OutputDirectory']) {
                $configuredOutput = [string]$configuration.OutputDirectory
                $reportRoot = if ([IO.Path]::IsPathRooted($configuredOutput)) {
                    [IO.Path]::GetFullPath($configuredOutput)
                } else {
                    [IO.Path]::GetFullPath((Join-Path $repoRoot $configuredOutput))
                }
            }
            Write-Host "Resultater: $reportRoot (programmets Artifacts-linje viser den konkrete koersel)."
            if ($script:performanceExitCode -ne 0) {
                Write-Host "Koerslen fejlede (exitkode $script:performanceExitCode). En mappe med rapportfiler betyder ikke, at belastningstesten blev gennemfoert." -ForegroundColor Yellow
            }
            if ($OpenReport -and (Test-Path -LiteralPath $reportRoot -PathType Container)) {
                $latestReport = Get-ChildItem -LiteralPath $reportRoot -Directory |
                    Where-Object { $_.CreationTimeUtc -ge $startedAt } |
                    ForEach-Object { Get-Item -LiteralPath (Join-Path $_.FullName 'report.html') -ErrorAction SilentlyContinue } |
                    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
                if ($latestReport) { Invoke-Item -LiteralPath $latestReport.FullName }
            }
        }
    }
} catch {
    Write-Host "FEJL: $($_.Exception.Message)" -ForegroundColor Red
    if ($script:performanceExitCode -eq 0) { $script:performanceExitCode = 1 }
} finally {
    foreach ($name in $originalEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], 'Process')
    }
    if ($locationChanged) { Pop-Location }
}
exit $script:performanceExitCode
