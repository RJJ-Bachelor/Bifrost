# Normal start: Start foerst Aspire, og koer derefter .\start-pwa.bat.
# Aspire konfigurerer automatisk Gnas lokale push-noegler.
[CmdletBinding()]
param(
    [string]$Domain = 'catapult-straddle-bagging.ngrok-free.dev',
    [int]$Port = 8080,
    [string]$GatewayUrl = 'http://localhost:6004'
)

$ErrorActionPreference = 'Stop'
$ngrokProcess = $null
Push-Location $PSScriptRoot

try {
    Write-Host "=== Kontrollerer gateway og Gnas push-konfiguration via $GatewayUrl ==="
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    foreach ($endpoint in @('/api/students/me', '/api/students/notifications/push/public-key')) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri ($GatewayUrl.TrimEnd('/') + $endpoint) -WebSession $session -TimeoutSec 15
        }
        catch {
            $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
            $reason = switch ($status) {
                503 {
                    if ($endpoint -like '*/push/*') {
                        'Push er deaktiveret, eller Gna er ikke klar. Genstart Aspire, og kontroller at Parameters:push-enabled er true i AppHost-konfigurationen.'
                    } else {
                        'Student-API er ikke klar. Kontroller Heimdalls og Eirs status/logs i Aspire.'
                    }
                }
                401 { 'Studentens cookie blev afvist. Kontroller den faelles student-cookiekonfiguration mellem Heimdall og Gna.' }
                403 { 'Studentens adgang blev afvist. Kontroller autentifikation mellem Heimdall og Gna.' }
                502 { 'Gatewayen kan ikke naa backendservicen. Kontroller Gnas og Eirs status/logs i Aspire.' }
                default { "Kontroller at Aspire koerer, og at gatewayen kan naas paa $GatewayUrl." }
            }
            throw "Kontrol af $endpoint fejlede (HTTP $status). $reason"
        }
    }
    $push = $response.Content | ConvertFrom-Json
    if (-not $push.publicKey) { throw 'Gna returnerede ikke en offentlig push-noegle.' }
    Write-Host 'Gateway, student-cookie og Gnas offentlige push-noegle svarer korrekt.'

    $ngrokCommand = Get-Command ngrok -CommandType Application -ErrorAction Stop
    Write-Host '=== Bygger testfrontenden ==='
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Build fejlede.' }
    $dist = Join-Path $PSScriptRoot 'dist/test-frontend/browser'
    if (-not (Test-Path (Join-Path $dist 'index.html'))) { throw "Fandt ikke index.html i $dist." }

    # Brug eksisterende ngrok-konfiguration eller NGROK_AUTHTOKEN fra miljoeet.
    # Ingen token gemmes i kildekoden, og kun denne scripts egen proces stoppes.
    Write-Host '=== Starter ngrok ==='
    $ngrokProcess = Start-Process -FilePath $ngrokCommand.Source -ArgumentList @('http', "--url=https://$Domain", $Port) `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput '.pwa-ngrok.stdout.log' -RedirectStandardError '.pwa-ngrok.stderr.log'
    if ($ngrokProcess.WaitForExit(1500)) {
        throw 'Ngrok kunne ikke starte. Se .pwa-ngrok.stderr.log. Konfigurer ngrok-auth uden for kildekoden.'
    }
    Write-Host "Aabn paa telefonen: https://$Domain"
    Write-Host 'Tryk Ctrl+C for at stoppe (lukker ogsaa ngrok).'
    & node (Join-Path $PSScriptRoot 'pwa-server.js') $dist $Port $GatewayUrl
    if ($LASTEXITCODE -ne 0) { throw 'PWA-serveren stoppede med en fejl.' }
}
finally {
    if ($ngrokProcess -and -not $ngrokProcess.HasExited) { Stop-Process -Id $ngrokProcess.Id }
    Pop-Location
}
