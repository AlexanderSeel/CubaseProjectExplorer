param(
    [Parameter(Mandatory=$true)][string]$Project,
    [string]$Cli = "$PSScriptRoot\..\src\CubaseInspector.Cli\bin\Release\net10.0\CubaseInspector.Cli.exe",
    [string]$OutputFolder = "$PWD\cpr-inspection"
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Project -PathType Leaf)) { throw "CPR file not found: $Project" }
if (-not (Test-Path -LiteralPath $Cli -PathType Leaf)) {
    throw "CLI executable not found: $Cli. Download the CLI-win-x64 artifact from successful GitHub Actions."
}
$source = (Resolve-Path -LiteralPath $Project).Path
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$structure = Join-Path $OutputFolder 'cpr-structure.json'
$overview = Join-Path $OutputFolder 'cpr-structure.md'
$inspection = Join-Path $OutputFolder 'cpr-inspect.md'
& $Cli 'cpr-structure' $source '--json' | Out-File -Encoding utf8 -LiteralPath $structure
if ($LASTEXITCODE -ne 0) { throw "Native CPR structure failed ($LASTEXITCODE)" }
& $Cli 'cpr-structure' $source '--markdown' | Out-File -Encoding utf8 -LiteralPath $overview
if ($LASTEXITCODE -ne 0) { throw "Native CPR Markdown export failed ($LASTEXITCODE)" }
& $Cli 'inspect' $source '--markdown' | Out-File -Encoding utf8 -LiteralPath $inspection
if ($LASTEXITCODE -ne 0) { throw "CPR evidence inspection failed ($LASTEXITCODE)" }
$data = Get-Content -LiteralPath $structure -Raw | ConvertFrom-Json
Write-Host "Read-only CPR inspection finished:"
Write-Host "  Version hint: $($data.VersionTextCandidate)"
Write-Host "  Chunks: $($data.Chunks.Count)"
Write-Host "  Candidate class records: $($data.Arrangement.RecordsFound)"
Write-Host "  Candidate tracks: $(@($data.Arrangement.TrackHints).Count)"
Write-Host "  Partial structure: $($data.Arrangement.Partial)"
Write-Host "  Reports: $OutputFolder"
