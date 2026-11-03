# User-local installation of the Uno Desktop build. No elevation is required.
# This is NOT a signed MSIX; Windows App SDK runtime prerequisites may still be needed.
[CmdletBinding()]
param(
    [string]$PackageDir = $PSScriptRoot,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\CubaseProjectExplorer'),
    [switch]$CheckOnly,
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$shortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Cubase Project Explorer.lnk'
$markerName = '.cubase-project-explorer-install.txt'
function Test-AppRoot([string]$candidate) {
    if ([string]::IsNullOrWhiteSpace($candidate) -or
        [string]::IsNullOrWhiteSpace([IO.Path]::GetFullPath($candidate))) {
        throw 'An absolute install path is required.'
    }
    return [IO.Path]::GetFullPath($candidate)
}
$target = Test-AppRoot $InstallDir
$localPrograms = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
if (-not $target.StartsWith($localPrograms + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "For safety this local installer only supports a subfolder under $localPrograms"
}
if ($target.TrimEnd('\').Equals($localPrograms.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to install into the shared Programs directory itself.'
}
if ($Uninstall) {
    if (-not (Test-Path -LiteralPath (Join-Path $target $markerName) -PathType Leaf)) {
        throw 'Uninstall refused: installation marker is absent; no files removed.'
    }
    if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
    Remove-Item -LiteralPath $target -Recurse -Force
    Write-Host "Removed user-local Cubase Project Explorer installation: $target"
    return
}
$source = [IO.Path]::GetFullPath($PackageDir)
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "Package directory not found: $source"
}
$executables = @(Get-ChildItem -LiteralPath $source -Filter 'CubaseInspector.App.exe' -Recurse -File)
if ($executables.Count -ne 1) {
    throw "Expected exactly one CubaseInspector.App.exe in the extracted package; found $($executables.Count)."
}
if ($target.StartsWith($source + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to copy a package into its own subfolder.'
}
$relativeExe = [IO.Path]::GetRelativePath($source, $executables[0].FullName)
Write-Host "Validated Uno Desktop executable: $relativeExe"
if ($CheckOnly) {
    Write-Host 'Package validation passed; installation and shell changes skipped.'
    return
}
if (Test-Path -LiteralPath $target -PathType Container) {
    if (-not (Test-Path -LiteralPath (Join-Path $target $markerName) -PathType Leaf)) {
        throw "Existing directory is not a recognized installation: $target"
    }
    # Do not overwrite a potentially running app. Require explicit uninstall first.
    throw "Existing installation found. Uninstall first, then install this version: $target"
}
New-Item -ItemType Directory -Force -Path $target | Out-Null
try {
    Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
    $installedExe = Join-Path $target $relativeExe
    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
        throw 'The installed executable is missing; installation did not complete.'
    }
    Set-Content -LiteralPath (Join-Path $target $markerName) -Value 'Cubase Project Explorer user-local installation' -Encoding UTF8
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installedExe
    $shortcut.WorkingDirectory = Split-Path -Path $installedExe -Parent
    $shortcut.Description = 'Cubase Project Explorer (read-only project inspector)'
    $shortcut.Save()
    Write-Host "Installed to $target and created a Start Menu shortcut."
    Write-Warning 'The Uno Desktop runtime is self-contained; this installer does not change any system-wide components.'
}
catch {
    if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue }
    throw
}
