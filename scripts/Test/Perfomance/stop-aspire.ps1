[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$appHostPath = (Resolve-Path -LiteralPath (Join-Path $repoRoot 'src/Bifrost/Orchestration/YggdrasilService/YggdrasilService.AppHost/YggdrasilService.AppHost.csproj')).Path

function Test-StaleCleanupOnly([string]$output) {
    $diagnosticOutput = $output
    $logMatch = [regex]::Match($output, 'See logs at (?<path>[^\r\n]+\.log)')
    if ($logMatch.Success -and (Test-Path -LiteralPath $logMatch.Groups['path'].Value.Trim())) {
        $diagnosticOutput += "`n" + (Get-Content -LiteralPath $logMatch.Groups['path'].Value.Trim() -Raw)
    }

    $resourceNames = [regex]::Matches($diagnosticOutput, 'resourceKey"?\s*:\s*"?containers/([^",}]+)') |
        ForEach-Object { $_.Groups[1].Value } |
        Select-Object -Unique
    if ($resourceNames.Count -eq 0) { return $false }

    $runningAppHosts = (& $aspireCommand.Source ps --format Json --non-interactive --nologo 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or ($runningAppHosts -ne '[]' -and $runningAppHosts -ne '')) {
        return $false
    }

    foreach ($resourceName in $resourceNames) {
        $containerIds = & docker.exe ps -aq --filter "name=$resourceName" 2>$null
        if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace(($containerIds | Out-String).Trim(' ', "`r", "`n"))) {
            return $false
        }
    }

    return $true
}

try {
    $aspireCommand = Get-Command aspire.cmd -ErrorAction SilentlyContinue
    if (-not $aspireCommand) { throw 'Aspire CLI mangler i PATH.' }
    if (-not (Get-Command docker.exe -ErrorAction SilentlyContinue)) {
        throw 'Docker CLI mangler i PATH.'
    }

    if ($DryRun) {
        Write-Host "aspire.cmd stop --apphost `"$appHostPath`" --force --non-interactive --nologo"
        exit 0
    }

    Push-Location -LiteralPath $repoRoot
    try {
        $lastExitCode = 0
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            $previousErrorActionPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                $stopOutput = & $aspireCommand.Source stop --apphost $appHostPath --force --non-interactive --nologo 2>&1 | Out-String
            } finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }
            $lastExitCode = $LASTEXITCODE
            Write-Host $stopOutput.TrimEnd()
            if ($lastExitCode -eq 0) { exit 0 }
            if (Test-StaleCleanupOnly $stopOutput) {
                Write-Host 'Aspire rapporterede stale cleanup, men de konkrete containere er allerede væk. Oprydning er afsluttet.' -ForegroundColor Yellow
                exit 0
            }
            if ($attempt -lt 3) {
                Write-Host "Aspire cleanup failed (attempt $attempt/3); retrying..."
                Start-Sleep -Seconds 2
            }
        }
        exit $lastExitCode
    } finally {
        Pop-Location
    }
} catch {
    Write-Host "FEJL ved stop af Aspire: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
