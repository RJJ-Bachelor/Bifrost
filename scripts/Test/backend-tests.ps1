param(
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [string]$LogPath,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$projects = @(
    'Backend/EirService/EirService.Test/EirService.Test.csproj',
    'Backend/GnaService/GnaService.Test/GnaService.Test.csproj',
    'Backend/Shared/Shared.Test/Shared.Test.csproj',
    'Backend/Backend.IntegrationTests/Backend.IntegrationTests.csproj',
    'Orchestration/HeimdallGateway.Test/HeimdallGateway.Test.csproj'
)
$failed = $false
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot "src/Bifrost/$project"
    $projectName = [IO.Path]::GetFileNameWithoutExtension($projectPath)
    $testArguments = @('test', $projectPath, '--collect:XPlat Code Coverage',
        '--settings', (Join-Path $PSScriptRoot 'coverage.runsettings'),
        '--results-directory', (Join-Path $ResultsDirectory $projectName),
        '--logger', 'trx', '--verbosity', 'minimal', '-m:1', '-nr:false', '--tl:off')
    if ($NoRestore) { $testArguments += '--no-restore' }
    $previousErrorAction = $ErrorActionPreference
    try {
        # Windows PowerShell 5 wraps native stderr in ErrorRecords even on exit 0.
        $ErrorActionPreference = 'Continue'
        if ($LogPath) {
            & dotnet @testArguments 2>&1 | Tee-Object -FilePath $LogPath -Append -ErrorAction Stop
        } else {
            & dotnet @testArguments
        }
        $testExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($testExitCode -ne 0) { $failed = $true }
}
if ($failed) { exit 1 }
exit 0
