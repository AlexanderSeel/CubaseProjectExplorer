#requires -Version 5.1
<#
.SYNOPSIS
    Starts the Git history of AlexanderSeel/CubaseProjectExplorer over with one
    root commit containing the exact current main tree, then tags it v1.0.0.

.DESCRIPTION
    Always operates on a freshly created bare clone. Never resets an existing
    developer working directory and never stages untracked or ignored files.

    The previous reachable history (all fetched branches and tags) is backed up
    to a verified Git bundle OUTSIDE the temporary bare repository first.

    Without -Execute, this is a safe preview: it prepares and verifies the
    backup, prints the intended operation, and leaves GitHub untouched.

    With -Execute, you must type the exact confirmation phrase to allow a
    guarded --force-with-lease update of refs/heads/main. The new tag is then
    pushed without force. A GitHub Release is optional.

    WARNING: Existing tags, other branches, pull requests, old commit links and
    cached GitHub references can still expose the earlier history. This is a
    clean replacement of main's ancestry, NOT secure erasure of old objects.

.EXAMPLE
    .\Reset-CubaseProjectExplorerHistory.ps1

.EXAMPLE
    .\Reset-CubaseProjectExplorerHistory.ps1 -Execute

.EXAMPLE
    .\Reset-CubaseProjectExplorerHistory.ps1 -Execute -CreateGitHubRelease

.EXAMPLE
    .\Reset-CubaseProjectExplorerHistory.ps1 -Execute -BackupDirectory 'D:\GitBackups'
#>
[CmdletBinding()]
param(
    [string]$BackupDirectory = '',
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version = 'v1.0.0',
    [switch]$Execute,
    [switch]$CreateGitHubRelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repository = 'AlexanderSeel/CubaseProjectExplorer'
$repoUrl = 'https://github.com/AlexanderSeel/CubaseProjectExplorer.git'
$branchRef = 'refs/heads/main'

function Invoke-Git {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git failed (exit $LASTEXITCODE): git $($Arguments -join ' ')"
    }
    return (($output | Out-String).Trim())
}

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

if ($CreateGitHubRelease -and -not $Execute) {
    throw '-CreateGitHubRelease requires -Execute.'
}
Assert-Command git
if ($CreateGitHubRelease) { Assert-Command gh }

if ([string]::IsNullOrWhiteSpace($BackupDirectory)) {
    $BackupDirectory = Join-Path $PSScriptRoot 'Git-History-Backups'
}
$BackupDirectory = [System.IO.Path]::GetFullPath($BackupDirectory)
New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$unique = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$clonePath = Join-Path ([System.IO.Path]::GetTempPath()) "CubaseProjectExplorer-history-reset-$stamp-$unique.git"
$bundlePath = Join-Path $BackupDirectory "CubaseProjectExplorer-before-$Version-$stamp-$unique.bundle"
$completed = $false

Write-Host "Repository : $repository" -ForegroundColor Cyan
Write-Host "Mode       : $(if ($Execute) { 'EXECUTE - remote history will be replaced' } else { 'PREVIEW - no changes to GitHub' })"
Write-Host "Backup     : $bundlePath"
Write-Host "Scratch    : $clonePath"
Write-Host ''

try {
    Write-Host '[1/7] Clone complete Git refs into a fresh bare repository ...'
    Invoke-Git -Arguments @('clone', '--bare', '--origin', 'origin', $repoUrl, $clonePath) | Out-Null

    $oldHead = Invoke-Git -Arguments @('-C', $clonePath, 'rev-parse', '--verify', $branchRef)
    $treeRef = "${oldHead}^{tree}"
    $tree = Invoke-Git -Arguments @('-C', $clonePath, 'rev-parse', '--verify', $treeRef)
    if ($oldHead -notmatch '^[0-9a-f]{40,64}$' -or $tree -notmatch '^[0-9a-f]{40,64}$') {
        throw 'Unexpected branch or tree object ID.'
    }

    Write-Host '[2/7] Create and verify recoverable history backup ...'
    Invoke-Git -Arguments @('-C', $clonePath, 'bundle', 'create', $bundlePath, '--all') | Out-Null
    Invoke-Git -Arguments @('-C', $clonePath, 'bundle', 'verify', $bundlePath) | Out-Null
    if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf) -or
        (Get-Item -LiteralPath $bundlePath).Length -le 0) {
        throw 'Backup bundle was not created successfully.'
    }

    Write-Host '[3/7] Check remote HEAD and existing release tag ...'
    $remoteLine = Invoke-Git -Arguments @('-C', $clonePath, 'ls-remote', 'origin', $branchRef)
    $remoteSha = @($remoteLine -split '\s+')[0]
    if ($remoteSha -ne $oldHead) {
        throw "Remote main moved during backup. Expected $oldHead; found $remoteSha. No remote changes made."
    }
    $existingTag = Invoke-Git -Arguments @('-C', $clonePath, 'ls-remote', '--refs', '--tags', 'origin', "refs/tags/$Version")
    if (-not [string]::IsNullOrWhiteSpace($existingTag)) {
        throw "Tag $Version already exists on GitHub. Refusing to overwrite it."
    }

    Write-Host ''
    Write-Host "Current main : $oldHead"
    Write-Host "Current tree : $tree"
    Write-Host "Backup bytes : $((Get-Item -LiteralPath $bundlePath).Length)"
    Write-Host ''

    if (-not $Execute) {
        Write-Host 'PREVIEW COMPLETE: GitHub has not been modified.' -ForegroundColor Green
        Write-Host "The complete backup is saved to: $bundlePath"
        Write-Host "To perform the history reset, run: .\Reset-CubaseProjectExplorerHistory.ps1 -Execute"
        $completed = $true
        return
    }

    # A root commit needs a configured author/committer identity. Validate before
    # prompting for a destructive action.
    & git -C $clonePath var GIT_AUTHOR_IDENT *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Git identity is not configured. Set git config --global user.name and user.email, then retry.'
    }

    Write-Warning 'This will REPLACE the entire commit ancestry of origin/main.'
    Write-Warning 'Everyone with a local clone must re-clone or explicitly realign their branch.'
    Write-Warning 'Other Git refs, old PR links and GitHub caches may keep old commits reachable.'
    $confirmationPhrase = "RESET MAIN $Version"
    $confirmation = Read-Host "Type exactly '$confirmationPhrase' to continue"
    if ($confirmation -cne $confirmationPhrase) {
        Write-Host 'Cancelled. No remote changes made. Backup is preserved.' -ForegroundColor Yellow
        $completed = $true
        return
    }

    Write-Host '[4/7] Create one root commit from the exact existing tree ...'
    # This root commit has NO parent. It contains every tracked file exactly
    # as present in the cloned main tree, without checkout, merge or copy.
    $newRoot = Invoke-Git -Arguments @('-C', $clonePath, 'commit-tree', $tree, '-m', "Initial release: CubaseProjectExplorer $Version")
    $newRoot = ($newRoot -split '\r?\n')[-1].Trim()
    if ($newRoot -notmatch '^[0-9a-f]{40,64}$') { throw 'Could not create a valid root commit.' }

    $newParents = Invoke-Git -Arguments @('-C', $clonePath, 'rev-list', '--parents', '-n', '1', $newRoot)
    if (@($newParents -split '\s+').Count -ne 1) { throw 'New commit unexpectedly has a parent.' }
    $newTree = Invoke-Git -Arguments @('-C', $clonePath, 'rev-parse', "${newRoot}^{tree}")
    if ($newTree -ne $tree) { throw 'New root commit tree differs from the original tree.' }

    Write-Host '[5/7] Create annotated version tag locally ...'
    Invoke-Git -Arguments @('-C', $clonePath, 'tag', '-a', $Version, $newRoot, '-m', "CubaseProjectExplorer $Version") | Out-Null

    Write-Host '[6/7] Guarded push of main, then push version tag ...'
    # The lease makes the push fail if anyone pushed to main since the clone.
    $lease = "--force-with-lease=${branchRef}:$oldHead"
    Invoke-Git -Arguments @('-C', $clonePath, 'push', $lease, 'origin', "${newRoot}:$branchRef") | Out-Null
    Invoke-Git -Arguments @('-C', $clonePath, 'update-ref', $branchRef, $newRoot, $oldHead) | Out-Null
    # Brace the parameter name because PowerShell otherwise parses `$Version:`
    # as a scoped variable reference and drops it from the refspec.
    Invoke-Git -Arguments @('-C', $clonePath, 'push', 'origin', "refs/tags/${Version}:refs/tags/${Version}") | Out-Null

    Write-Host '[7/7] Verify the remote now points to the new root commit ...'
    $confirmedLine = Invoke-Git -Arguments @('-C', $clonePath, 'ls-remote', 'origin', $branchRef)
    $confirmedSha = @($confirmedLine -split '\s+')[0]
    if ($confirmedSha -ne $newRoot) {
        throw "Remote verification failed: expected $newRoot; found $confirmedSha."
    }
    $tagLine = Invoke-Git -Arguments @('-C', $clonePath, 'ls-remote', '--refs', '--tags', 'origin', "refs/tags/$Version")
    if ([string]::IsNullOrWhiteSpace($tagLine)) {
        throw "Remote tag $Version could not be verified."
    }

    Write-Host ''
    Write-Host 'SUCCESS: main now has a single root commit in its history.' -ForegroundColor Green
    Write-Host "New root commit: $newRoot"
    Write-Host "Published tag  : $Version"
    Write-Host "History backup : $bundlePath"
    Write-Host "Commit URL     : https://github.com/$repository/commit/$newRoot"

    if ($CreateGitHubRelease) {
        Write-Host 'Creating GitHub Release ...'
        & gh release create $Version '--repo' $repository '--verify-tag' '--title' "CubaseProjectExplorer $Version" '--notes' 'Initial public release of CubaseProjectExplorer.'
        if ($LASTEXITCODE -ne 0) {
            throw 'The reset and version tag succeeded, but GitHub Release creation failed. Create the release manually; do NOT rerun the reset.'
        }
        Write-Host "Release URL: https://github.com/$repository/releases/tag/$Version"
    }
    $completed = $true
}
finally {
    # The verified Git bundle lives outside this disposable clone and persists.
    # Keep the clone after an error so the backup/root/tag can be inspected.
    if ($completed -and (Test-Path -LiteralPath $clonePath)) {
        try { Remove-Item -LiteralPath $clonePath -Recurse -Force -ErrorAction Stop }
        catch { Write-Warning "Could not remove temporary clone: $clonePath" }
    }
}
