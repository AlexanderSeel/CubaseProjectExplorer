# Read-only private CPR / BAK fixture collection. Run from the repository root.
# Never copies binary fixtures into the repository or uploads them to CI.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$Cli,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$manifestDir = Split-Path -Parent $manifestPath
$cliPath = (Resolve-Path -LiteralPath $Cli).Path
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outDir = (Resolve-Path -LiteralPath $OutputDirectory).Path
$rows = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
if ($rows.Count -eq 0) { throw "Manifest contains no fixtures." }
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$summary = foreach ($fixture in $rows) {
    $id = [string]$fixture.id
    if ($id -notmatch '^[a-zA-Z0-9_-]{1,64}$' -or !$seen.Add($id)) {
        throw "Fixture ID must be unique and safe: $id"
    }
    $relative = [string]$fixture.file
    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative)) {
        throw "Fixture path must be relative to manifest directory: $id"
    }
    $fullPath = [IO.Path]::GetFullPath((Join-Path $manifestDir $relative))
    $prefix = $manifestDir.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Fixture path must stay under private manifest directory: $id"
    }
    $extension = [IO.Path]::GetExtension($fullPath)
    if ($extension -notin @('.cpr', '.bak', '.dawproject')) { throw "Unsupported fixture extension: $id" }
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Missing fixture: $id" }
    $hash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected = [string]$fixture.sha256
    if ($expected -notmatch '^[a-fA-F0-9]{64}$' -or $hash -ne $expected.ToLowerInvariant()) {
        throw "Fixture hash mismatch: $id. Do not compare results from an unverified file."
    }
    $format = if ($extension -eq '.dawproject') { 'inspect' } else { 'cpr-structure' }
    $status = [string]$fixture.expectedStatus
    if ($status -notin @('readable', 'invalid')) { throw "expectedStatus must be readable or invalid: $id" }
    $reportFile = Join-Path $outDir "$id.json"
    $diagnosticFile = Join-Path $outDir "$id.stderr.txt"
    # Keep process output separate from error output; do not modify input files.
    & $cliPath $format $fullPath --json 1> $reportFile 2> $diagnosticFile
    $exit = $LASTEXITCODE
    $coverageReport = "$id.coverage.json"
    if ($exit -eq 0) {
        & $cliPath inspect $fullPath --coverage-json 1> (Join-Path $outDir $coverageReport) 2> (Join-Path $outDir "$id.coverage.stderr.txt")
        $coverageExit = $LASTEXITCODE
    } else {
        $coverageExit = $null
        $coverageReport = ''
    }
    $reason = ''
    if ($null -ne $coverageExit -and $coverageExit -ne 0) {
        $reason = 'Structural inspection succeeded but normalized field coverage failed'
    }
    if ($status -eq 'readable' -and $exit -ne 0) { $reason = 'Expected readable file but CLI rejected it' }
    if ($status -eq 'invalid' -and $exit -eq 0) { $reason = 'Expected invalid file but CLI accepted it' }
    $expectedErrorCode = [string]$fixture.expectedErrorCode
    if ($status -eq 'invalid' -and $exit -ne 0 -and
        ![string]::IsNullOrWhiteSpace($expectedErrorCode)) {
        if ($expectedErrorCode -notmatch '^[A-Z0-9_]{2,64}$') {
            throw "Invalid expectedErrorCode for $id"
        }
        $stderr = Get-Content -LiteralPath $diagnosticFile -Raw
        if (!$stderr.Contains($expectedErrorCode + ':', [StringComparison]::Ordinal)) {
            $reason = "Unexpected parser error code; expected $expectedErrorCode"
        }
    }
    if ($exit -eq 0 -and $status -eq 'readable' -and $extension -ne '.dawproject') {
        try {
            $actual = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
            if ($null -ne $fixture.expectedChunks -and
                $actual.Chunks.Count -ne [int]$fixture.expectedChunks) {
                $reason = 'Chunk count mismatch'
            }
            if ($null -ne $fixture.expectedTrackHints -and
                $actual.Arrangement.TrackHints.Count -ne [int]$fixture.expectedTrackHints) {
                $reason = 'Candidate track hint count mismatch'
            }
            if ($null -ne $fixture.expectedClassRecords -and
                $actual.Arrangement.RecordsFound -ne [int]$fixture.expectedClassRecords) {
                $reason = 'Class record count mismatch'
            }
        }
        catch { $reason = 'Unable to parse CLI structure JSON: ' + $_.Exception.GetType().Name }
    }
    [pscustomobject]@{
        Id = $id
        Format = $extension
        ExpectedSavedBy = [string]$fixture.savedBy
        ExpectedStatus = [string]$fixture.expectedStatus
        ExpectedErrorCode = $expectedErrorCode
        ExpectedTracks = $fixture.expectedTracks
        ExpectedPlugins = $fixture.expectedPlugins
        ExpectedRouting = [string]$fixture.expectedRouting
        Sha256 = $hash
        ExitCode = $exit
        Outcome = if ($reason.Length -eq 0) { 'Pass' } else { 'Mismatch' }
        Reason = $reason
        Report = "$id.json"
        CoverageReport = $coverageReport
    }
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outDir 'fixture-results.json') -Encoding utf8
$summary | Export-Csv -LiteralPath (Join-Path $outDir 'fixture-results.csv') -NoTypeInformation -Encoding utf8
Write-Host "Processed $($summary.Count) private fixtures. Compare expected fields against JSON reports and Cubase UI before promoting any evidence."
$mismatches = @($summary | Where-Object Outcome -eq 'Mismatch')
foreach ($failure in $mismatches) {
    Write-Error -Message ("Fixture " + $failure.Id + ": " + $failure.Reason +
        " (exit " + $failure.ExitCode + ", expected " + $failure.ExpectedStatus + ")") -ErrorAction Continue
}
if ($mismatches.Count -gt 0) { exit 1 }
# The intentionally invalid fixture leaves LASTEXITCODE nonzero; successful
# validation must explicitly clear it for callers and GitHub Actions.
exit 0
