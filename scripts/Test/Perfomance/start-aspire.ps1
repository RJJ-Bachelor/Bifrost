[CmdletBinding()]
param(
    [ValidateRange(1, 1800)]
    [int]$TimeoutSeconds = 180,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$appHostPath = (Resolve-Path -LiteralPath (Join-Path $repoRoot 'src/Bifrost/Orchestration/YggdrasilService/YggdrasilService.AppHost/YggdrasilService.AppHost.csproj')).Path
$requiredResources = @('postgres', 'rabbitmq', 'varservice', 'eirservice-api', 'gnaservice-api', 'mimirservice-api', 'heimdallgateway')
$gatewayUrl = 'http://localhost:6004'
$script:aspireExitCode = 0
$locationChanged = $false

function Invoke-AspireCommand {
    param([string[]]$Arguments, [switch]$CaptureOutput)
    $previousPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 can wrap native stderr even when the command succeeds.
        $ErrorActionPreference = 'Continue'
        if ($CaptureOutput) {
            $commandOutput = & $aspireCommand.Source @Arguments 2>&1
        } else {
            & $aspireCommand.Source @Arguments
        }
        $commandExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($commandExitCode -ne 0) {
        $script:aspireExitCode = $commandExitCode
        throw "Aspire $($Arguments[0]) afsluttede med exitkode $commandExitCode. Kontroller Docker Desktop og Aspire-dashboardets fejl."
    }
    if ($CaptureOutput) { return $commandOutput }
}

function Wait-ForGateway {
    param([int]$Timeout)
    $deadline = [DateTime]::UtcNow.AddSeconds($Timeout)
    do {
        try {
            $response = Invoke-WebRequest -Uri "$gatewayUrl/api/students/me" -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return }
        } catch {
            # The gateway can start before its backend services finish migrations.
        }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Gatewayen svarer ikke korrekt paa $gatewayUrl. En eller flere backendservices er sandsynligvis stoppet under opstart; kontroller Aspire-dashboardets logs og koer start-aspire.cmd igen."
}

try {
    Write-Host "AppHost: $appHostPath"
    if ($DryRun) {
        Write-Host 'aspire.cmd ps --format Json --non-interactive --nologo'
        Write-Host "Hvis denne AppHost ikke allerede koerer: aspire.cmd start --apphost `"$appHostPath`" --non-interactive"
        foreach ($resource in $requiredResources) {
            Write-Host "aspire.cmd wait $resource --apphost `"$appHostPath`" --timeout $TimeoutSeconds --non-interactive"
        }
        Write-Host 'DryRun: ingen opstart, netvaerkskald eller login.'
    } else {
        $aspireCommand = Get-Command aspire.cmd -ErrorAction SilentlyContinue
        if (-not $aspireCommand) { throw 'Aspire CLI mangler i PATH. Installer Aspire CLI, og aabn terminalen igen.' }
        Push-Location -LiteralPath $repoRoot
        $locationChanged = $true

        # Do not restart an existing AppHost or expose its dashboard login token from JSON.
        $runningHosts = (Invoke-AspireCommand -Arguments @('ps', '--format', 'Json', '--non-interactive', '--nologo') -CaptureOutput) -join [Environment]::NewLine | ConvertFrom-Json
        $alreadyRunning = @($runningHosts | Where-Object { $_.appHostPath -eq $appHostPath }).Count -gt 0
        if ($alreadyRunning) {
            Write-Host 'Yggdrasil koerer allerede; kontrollerer de noedvendige ressourcer.'
        } else {
            $dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
            if (-not $dockerCommand) { throw 'Docker CLI mangler. Start eller installer Docker Desktop foer Yggdrasil.' }
            $previousPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                $dockerVersion = & $dockerCommand.Source version --format '{{.Server.Version}}' 2>&1
                $dockerExitCode = $LASTEXITCODE
            } finally {
                $ErrorActionPreference = $previousPreference
            }
            if ($dockerExitCode -ne 0) { throw 'Docker er ikke klar. Start Docker Desktop, vent paa at motoren koerer, og proev start-aspire.cmd igen.' }

            Invoke-AspireCommand -Arguments @('start', '--apphost', $appHostPath, '--non-interactive')
        }

        foreach ($resource in $requiredResources) {
            Invoke-AspireCommand -Arguments @('wait', $resource, '--apphost', $appHostPath, '--timeout', "$TimeoutSeconds", '--non-interactive')
        }
        Write-Host "Kontrollerer gatewayens student-endpoint paa $gatewayUrl..."
        Wait-ForGateway -Timeout $TimeoutSeconds
        Write-Host 'Yggdrasil er klar. Koer check.cmd, derefter smoke.cmd og til sidst full.cmd.' -ForegroundColor Green
    }
} catch {
    Write-Host "FEJL: $($_.Exception.Message)" -ForegroundColor Red
    if ($script:aspireExitCode -eq 0) { $script:aspireExitCode = 1 }
} finally {
    if ($locationChanged) { Pop-Location }
}
exit $script:aspireExitCode
