<#
.SYNOPSIS
Guides manual DAWproject exports in Cubase; does not perform unattended conversion.
.DESCRIPTION
Opens each CPR in Cubase. You must choose File > Export > DAWproject and save
to the displayed destination. No documented Cubase DAWproject export CLI has
been verified. File-size stability indicates file detection, not export validity.
#>
[CmdletBinding()]
param(
    [string]$Root = 'f:\Cubase\Cubase',
    [string]$ExportRoot,
    [string]$CubaseExe,
    [string]$ManifestCsv,
    [switch]$ListOnly,
    [ValidateRange(1, 1440)]
    [int]$ExportTimeoutMinutes = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-CubaseExecutable {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        $resolved = (Resolve-Path -LiteralPath $ExplicitPath -ErrorAction Stop).Path
        if ((Get-Item -LiteralPath $resolved).PSIsContainer) { throw "CubaseExe must be an executable file: $resolved" }
        return $resolved
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'Steinberg\Cubase 15\Cubase15.exe')        
    )
    if (${env:ProgramFiles(x86)}) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} 'Steinberg\Cubase 15\Cubase15.exe'
    }
    $found = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $found) {
        $steinbergRoot = Join-Path $env:ProgramFiles 'Steinberg'
        if (Test-Path -LiteralPath $steinbergRoot -PathType Container) {
            $found = Get-ChildItem -LiteralPath $steinbergRoot -Directory -Filter 'Cubase 15*' -ErrorAction SilentlyContinue |
                ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -File -Filter 'Cubase*.exe' -ErrorAction SilentlyContinue } |
                Select-Object -First 1 -ExpandProperty FullName
        }
    }
    if (-not $found) {
        throw 'Could not locate Cubase 15. Supply -CubaseExe with the full path to Cubase15.exe.'
    }
    return (Resolve-Path -LiteralPath $found).Path
}

function Get-ProjectFiles {
    param([string]$ProjectRoot, [string]$CsvPath)

    if ($CsvPath) {
        $rows = Import-Csv -LiteralPath $CsvPath
        $paths = foreach ($row in $rows) {
            if ($row.FileType -and $row.FileType -ne 'CPR') { continue }
            if ($row.SourceFileExists -and $row.SourceFileExists -notin @('True', 'true', '1')) { continue }
            if (-not $row.SourceFilePath) { continue }
            $row.SourceFilePath
        }
    }
    else {
        $paths = Get-ChildItem -LiteralPath $ProjectRoot -Filter '*.cpr' -File -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object FullName
    }

    $rootFull = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\') + '\'
    $rootPrefix = $rootFull
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $paths) {
        try { $fullPath = [IO.Path]::GetFullPath($path) }
        catch { Write-Warning "Skipping invalid project path: $path"; continue }
        if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            Write-Warning "Skipping path outside -Root: $fullPath"
            continue
        }
        if ([IO.Path]::GetExtension($fullPath) -ine '.cpr' -or -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
        if ($seen.Add($fullPath)) { $fullPath }
    }
}

function Wait-ForStableExport {
    param([string]$Path, [int]$TimeoutMinutes)

    $deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
    $nextStatus = [DateTime]::UtcNow
    $previousLength = -1L
    $stableReads = 0
    while ([DateTime]::UtcNow -lt $deadline) {
        if ([DateTime]::UtcNow -ge $nextStatus) {
            $remainingMinutes = [Math]::Ceiling(($deadline - [DateTime]::UtcNow).TotalMinutes)
            Write-Host "Watching export file ($remainingMinutes min remaining). If you have not clicked Save, export manually in Cubase. Ctrl+C stops this script."
            $nextStatus = [DateTime]::UtcNow.AddSeconds(15)
        }
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            try {
                $length = (Get-Item -LiteralPath $Path).Length
                if ($length -gt 0 -and $length -eq $previousLength) { $stableReads++ } else { $stableReads = 0 }
                if ($stableReads -ge 3) { return $true }
                $previousLength = $length
            }
            catch { $stableReads = 0 }
        }
        Start-Sleep -Seconds 1
    }
    return $false
}

$rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
if (-not $ExportRoot) { $ExportRoot = Join-Path $rootPath '_DawprojectExports' }
$exportPath = [IO.Path]::GetFullPath($ExportRoot)
if ($exportPath.StartsWith(([IO.Path]::GetFullPath($rootPath).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase) -eq $false) {
    throw "ExportRoot must be inside Root to keep the batch outputs together. Root: $rootPath"
}
if ($ManifestCsv) { $ManifestCsv = (Resolve-Path -LiteralPath $ManifestCsv -ErrorAction Stop).Path }
$projects = @(Get-ProjectFiles -ProjectRoot $rootPath -CsvPath $ManifestCsv)
if ($projects.Count -eq 0) { throw "No existing CPR files were found under $rootPath." }

$jobs = foreach ($project in $projects) {
    $relativeDirectory = [IO.Path]::GetRelativePath($rootPath, (Split-Path -Parent $project))
    $targetDirectory = if ($relativeDirectory -eq '.') { $exportPath } else { Join-Path $exportPath $relativeDirectory }
    [pscustomobject]@{
        Source = $project
        TargetDirectory = $targetDirectory
        Target = Join-Path $targetDirectory (([IO.Path]::GetFileNameWithoutExtension($project)) + '.dawproject')
    }
}

Write-Host "Projects in batch: $($jobs.Count)"
Write-Host "Source folder:     $rootPath"
Write-Host "Export folder:     $exportPath"
if ($ListOnly) {
    $jobs | Select-Object Source, Target | Format-Table -AutoSize
    return
}

$cubasePath = Resolve-CubaseExecutable -ExplicitPath $CubaseExe
New-Item -ItemType Directory -Path $exportPath -Force | Out-Null
$auditPath = Join-Path $exportPath ('export-audit-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.csv')
$audit = [Collections.Generic.List[object]]::new()

Write-Host ''
Write-Host 'MANUAL EXPORT REQUIRED: this script opens projects but does not trigger Cubase export.' -ForegroundColor Yellow
Write-Host 'For each project, wait until it is fully open, choose File > Export > DAWproject, and save to the exact path shown.'
Write-Host 'Do not save changes to the original CPR. Close the current project without saving before advancing.'
Write-Host ''

for ($index = 0; $index -lt $jobs.Count; $index++) {
    $job = $jobs[$index]
    if (Test-Path -LiteralPath $job.Target -PathType Leaf) {
        Write-Host "[$($index + 1)/$($jobs.Count)] Already exists, leaving untouched: $($job.Target)" -ForegroundColor DarkYellow
        $audit.Add([pscustomobject]@{ Source = $job.Source; Export = $job.Target; Result = 'SkippedExisting'; TimeUtc = [DateTime]::UtcNow.ToString('o') })
        continue
    }

    New-Item -ItemType Directory -Path $job.TargetDirectory -Force | Out-Null
    Write-Progress -Activity 'Export Cubase projects to DAWproject' -Status "Opening $($index + 1) of $($jobs.Count): $(Split-Path -Leaf $job.Source)" -PercentComplete (($index / $jobs.Count) * 100)
    Write-Host "[$($index + 1)/$($jobs.Count)] $($job.Source)" -ForegroundColor Cyan
    Write-Host "Save DAWproject as: $($job.Target)"

    $quotedProjectPath = '"' + $job.Source.Replace('"', '\"') + '"'
    Start-Process -FilePath $cubasePath -ArgumentList $quotedProjectPath | Out-Null
    Write-Host 'In Cubase:' -ForegroundColor Yellow
    Write-Host '  1. Wait for the project to open and handle any missing plug-in/media dialogs.'
    Write-Host '  2. Choose File > Export > DAWproject (Datei > Exportieren > DAWproject).'
    Write-Host "  3. Use this full destination in the save dialog: $($job.Target)"
    Write-Host '  4. Click Save. Do not save changes to the original CPR.'
    [void](Read-Host 'AFTER clicking Save in Cubase, press Enter here to check the exported file (Ctrl+C to stop)')
    Write-Host 'Checking for a non-empty file whose size remains stable; this does not trigger an export.'

    if (Wait-ForStableExport -Path $job.Target -TimeoutMinutes $ExportTimeoutMinutes) {
        Write-Host "Stable file detected (contents not validated): $($job.Target)" -ForegroundColor Green
        $audit.Add([pscustomobject]@{ Source = $job.Source; Export = $job.Target; Result = 'Exported'; TimeUtc = [DateTime]::UtcNow.ToString('o') })
        [void](Read-Host 'Close this Cubase project without saving, then press Enter for the next project')
    }
    else {
        Write-Warning "No stable export appeared before timeout: $($job.Target)"
        $choice = Read-Host 'Enter R to retry this project, S to skip, or Q to stop the batch'
        if ($choice -match '^[Rr]$') { $index--; continue }
        $audit.Add([pscustomobject]@{ Source = $job.Source; Export = $job.Target; Result = 'SkippedTimeout'; TimeUtc = [DateTime]::UtcNow.ToString('o') })
        if ($choice -match '^[Qq]$') { break }
    }
    $audit | Export-Csv -LiteralPath $auditPath -NoTypeInformation -Encoding utf8
}

Write-Progress -Activity 'Export Cubase projects to DAWproject' -Completed
$audit | Export-Csv -LiteralPath $auditPath -NoTypeInformation -Encoding utf8
Write-Host "Batch finished. Audit: $auditPath"
