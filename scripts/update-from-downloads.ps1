<#
.SYNOPSIS
    Update an installed ArtiMax PDF Editor from a release ZIP in Downloads.

.DESCRIPTION
    The "proper Update" path, as opposed to re-running Setup.exe as if it were
    a fresh install. Takes the ArtiMaxPDFEditor-<ver>-win-x64.zip from a GitHub
    Release, overlays it onto the existing installation, and restarts the app.
    No admin rights, no installer wizard, no re-ticking of install options.

    Setup.exe is still the right tool for a FIRST install -- it creates the
    Start Menu / desktop shortcuts, the Add/Remove Programs entry and
    (optionally) the .pdf association. This script only refreshes the payload
    of an install that already exists, which is why it refuses to run when it
    can't find one.

    WHAT IT NEVER TOUCHES
    ---------------------
    All per-user state lives OUTSIDE the install folder, under
    %APPDATA%\ArtiMaxPDFEditor\ -- view.json, toolbar.json, theme.json and the
    signatures library. An update cannot disturb any of it.

    Inside the install folder the copy is an OVERLAY, not a replace: files the
    ZIP doesn't carry are left alone. That deliberately preserves extra OCR
    languages downloaded into tessdata\ after install. The flip side is that a
    file dropped from the build stays behind until the next Setup.exe run.

.PARAMETER ZipDir
    Folder to search for the newest ArtiMaxPDFEditor-*.zip. Defaults to the
    current user's Downloads folder.

.PARAMETER Zip
    Use this exact ZIP instead of searching.

.PARAMETER InstallDir
    Override the install folder. Normally discovered from the uninstall
    registry key written by Setup.exe, then from the default install paths.

.PARAMETER NoBackup
    Skip the pre-update backup copy of the current install (saves a couple of
    hundred MB of disk and a few seconds).

.PARAMETER NoLaunch
    Don't start the app when the update finishes.

.PARAMETER Force
    Don't ask before closing a running copy of the editor, and allow
    installing a version the same as or older than the installed one.

.PARAMETER WhatIf
    Report what would change and write nothing.

.EXAMPLE
    .\update-from-downloads.ps1
    .\update-from-downloads.ps1 -Zip C:\Users\me\Downloads\ArtiMaxPDFEditor-1.0.36-win-x64.zip
    .\update-from-downloads.ps1 -WhatIf
#>
[CmdletBinding()]
param(
    [string]$ZipDir = (Join-Path $env:USERPROFILE 'Downloads'),
    [string]$Zip = '',
    [string]$InstallDir = '',
    [switch]$NoBackup,
    [switch]$NoLaunch,
    [switch]$Force,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$ExeName  = 'ArtiMaxPDFEditor.exe'
$ProcName = 'ArtiMaxPDFEditor'

# Inno Setup's AppId from installer\ArtiMaxPDFEditor.iss, plus the "_is1"
# suffix Inno appends to the uninstall key name. Must stay in step with the
# AppId there -- that GUID is what makes an upgrade an upgrade.
$UninstallKey = '{B7C4E8F0-1A2B-4C3D-9E8F-6A5B4C3D2E1F}_is1'

$UninstallRoots = @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
)

function Fail($msg) {
    Write-Host ''
    Write-Host $msg -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'ArtiMax PDF Editor -- update' -ForegroundColor Cyan
Write-Host '----------------------------'
if ($WhatIf) { Write-Host '(WhatIf -- nothing will be written)' -ForegroundColor Cyan }

# --- 1. Find the installation ----------------------------------------------
function Find-InstallDir {
    if ($InstallDir) {
        # An explicit override still has to point at a real install, or the
        # version read below fails with a raw Get-Item error.
        $d = $InstallDir.TrimEnd('\')
        if (-not (Test-Path -LiteralPath (Join-Path $d $ExeName))) {
            Fail "No $ExeName in the folder passed with -InstallDir:
  $d

Nothing was changed."
        }
        return $d
    }

    foreach ($r in $UninstallRoots) {
        $p = Join-Path $r $UninstallKey
        if (Test-Path -LiteralPath $p) {
            $loc = (Get-ItemProperty -LiteralPath $p -ErrorAction SilentlyContinue).InstallLocation
            if ($loc -and (Test-Path -LiteralPath (Join-Path $loc $ExeName))) { return $loc.TrimEnd('\') }
        }
    }

    # Not registered (or the registry has been cleaned) -- try the places
    # Setup.exe installs to: per-user first, since that's the default.
    $guesses = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\ArtiMax\PDF Editor'),
        (Join-Path $env:ProgramFiles 'ArtiMax\PDF Editor')
    )
    if (${env:ProgramFiles(x86)}) { $guesses += (Join-Path ${env:ProgramFiles(x86)} 'ArtiMax\PDF Editor') }
    foreach ($d in $guesses) {
        if ($d -and (Test-Path -LiteralPath (Join-Path $d $ExeName))) { return $d.TrimEnd('\') }
    }
    return $null
}

$dest = Find-InstallDir
if (-not $dest) {
    Fail "Can't find an existing ArtiMax PDF Editor installation.

This script updates an install that Setup.exe has already made. For a first
install, run ArtiMaxPDFEditor-Setup-<version>.exe instead -- it creates the
shortcuts and the uninstall entry, and this script can update it from then on.

If it IS installed somewhere unusual, point the script at it:
  .\update-from-downloads.ps1 -InstallDir ""D:\Apps\ArtiMax\PDF Editor"""
}

$destExe = Join-Path $dest $ExeName
$curVer  = (Get-Item -LiteralPath $destExe).VersionInfo.FileVersion
Write-Host ("Installed at:   {0}" -f $dest)
Write-Host ("Version now:    {0}" -f $curVer)

# A per-user install (the Setup.exe default) lives under %LOCALAPPDATA% and
# needs no elevation. A per-machine install lands in Program Files, where
# only an administrator can write -- check that now rather than after
# unpacking 90 MB and closing the user's editor.
$probe = Join-Path $dest (".artimax-write-test-" + [guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType File -Path $probe -Force -ErrorAction Stop | Out-Null
    Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
} catch {
    Fail "No permission to write to:
  $dest

That is a per-machine install, so the update needs administrator rights.
Right-click 'Update ArtiMax PDF Editor.bat' and choose 'Run as administrator'.
Nothing was changed."
}

# --- 2. Locate the ZIP ------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Zip)) {
    if (-not (Test-Path -LiteralPath $ZipDir)) { Fail "Download folder not found: $ZipDir" }
    $cand = Get-ChildItem -LiteralPath $ZipDir -Filter 'ArtiMaxPDFEditor-*.zip' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $cand) {
        Fail "No ArtiMaxPDFEditor-*.zip found in:
  $ZipDir

Download one first:
  https://github.com/MikeyBorin/PDFEditor/releases/latest
and pick the ArtiMaxPDFEditor-<version>-win-x64.zip asset."
    }
    $Zip = $cand.FullName
} elseif (-not (Test-Path -LiteralPath $Zip)) {
    Fail "ZIP not found: $Zip"
}

$zipItem = Get-Item -LiteralPath $Zip
Write-Host ("Update ZIP:     {0}  ({1:yyyy-MM-dd HH:mm}, {2:N0} MB)" -f `
    $zipItem.Name, $zipItem.LastWriteTime, ($zipItem.Length / 1MB))

Unblock-File -LiteralPath $Zip

# --- 3. Extract and sanity-check -------------------------------------------
$stage = Join-Path $env:TEMP ('artimax-update-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
Expand-Archive -LiteralPath $Zip -DestinationPath $stage -Force

try {
    # publish.ps1 zips the staging folder's CONTENTS, so the exe sits at the
    # root. Tolerate a hand-made zip with one wrapper folder as well.
    $src = $stage
    if (-not (Test-Path -LiteralPath (Join-Path $src $ExeName))) {
        $inner = Get-ChildItem -LiteralPath $stage -Directory |
                 Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName $ExeName) } |
                 Select-Object -First 1
        if ($inner) { $src = $inner.FullName }
    }

    # A truncated or wrong download must not dribble half a build over a
    # working installation.
    foreach ($required in @($ExeName, 'ArtiMaxPDFEditor.dll', 'pdfium.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $src $required))) {
            Fail "That ZIP doesn't look like an ArtiMax PDF Editor build (missing '$required').
Nothing was changed."
        }
    }

    $newVer = (Get-Item -LiteralPath (Join-Path $src $ExeName)).VersionInfo.FileVersion
    Write-Host ("Version in ZIP: {0}" -f $newVer)

    $cmp = $null
    try { $cmp = ([version]$newVer).CompareTo([version]$curVer) } catch { }
    if ($null -ne $cmp -and $cmp -le 0 -and -not $Force) {
        $what = if ($cmp -eq 0) { 'the same version as' } else { 'an OLDER version than' }
        Fail "The ZIP holds $what the installed copy ($newVer vs $curVer).
Nothing was changed. Re-run with -Force if that's really what you want."
    }

    Get-ChildItem -Recurse -LiteralPath $src -File | Unblock-File

    # --- 4. Close the running app -------------------------------------------
    $running = @(Get-Process -Name $ProcName -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0 -and -not $WhatIf) {
        Write-Host ''
        Write-Host ("PDF Editor is running ({0} window(s)). It has to close to be updated." -f $running.Count) -ForegroundColor Yellow
        if (-not $Force) {
            $ans = Read-Host 'Close it and continue? [Y/n]'
            if ($ans -and $ans -notmatch '^(y|yes)$') { Fail 'Cancelled. Nothing was changed.' }
        }
        Write-Host 'Closing PDF Editor...'
        foreach ($p in $running) {
            try { $null = $p.CloseMainWindow(); $null = $p.WaitForExit(5000) } catch { }
        }
        Get-Process -Name $ProcName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 800
        if (Get-Process -Name $ProcName -ErrorAction SilentlyContinue) {
            Fail 'PDF Editor is still running -- close it by hand and run this again. Nothing was changed.'
        }
    }

    # --- 5. Back up the current install -------------------------------------
    if (-not $NoBackup -and -not $WhatIf) {
        $backupRoot = Join-Path $env:LOCALAPPDATA 'ArtiMax\PDF Editor backups'
        $backup = Join-Path $backupRoot ("{0}-{1}" -f $curVer, (Get-Date -Format 'yyyyMMdd-HHmmss'))
        Write-Host ("Backing up to:  {0}" -f $backup)
        New-Item -ItemType Directory -Path $backup -Force | Out-Null
        Copy-Item -Path (Join-Path $dest '*') -Destination $backup -Recurse -Force
        # One backup is enough to roll back a bad update, and the install is a
        # couple of hundred MB -- so older ones are pruned rather than piling up.
        Get-ChildItem -LiteralPath $backupRoot -Directory |
            Sort-Object Name -Descending | Select-Object -Skip 1 |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # --- 6. Overlay ----------------------------------------------------------
    $srcLen  = $src.TrimEnd('\').Length + 1
    $updated = 0
    $added   = 0
    foreach ($file in Get-ChildItem -Recurse -LiteralPath $src -File) {
        $rel    = $file.FullName.Substring($srcLen)
        $target = Join-Path $dest $rel
        $isNew  = -not (Test-Path -LiteralPath $target)

        if ($WhatIf) {
            if ($isNew) { Write-Host "  would add:    $rel" -ForegroundColor Green }
            else        { Write-Host "  would update: $rel" }
        } else {
            $targetDir = Split-Path -Parent $target
            if (-not (Test-Path -LiteralPath $targetDir)) { New-Item $targetDir -ItemType Directory -Force | Out-Null }
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
        if ($isNew) { $added++ } else { $updated++ }
    }

    Write-Host ''
    Write-Host ("Files updated:  {0}" -f $updated)
    Write-Host ("Files added:    {0}" -f $added)

    if ($WhatIf) {
        Write-Host ''
        Write-Host 'WhatIf complete -- nothing was written.' -ForegroundColor Cyan
        return
    }

    # --- 7. Keep Add/Remove Programs honest ---------------------------------
    foreach ($r in $UninstallRoots) {
        $p = Join-Path $r $UninstallKey
        if (Test-Path -LiteralPath $p) {
            try {
                Set-ItemProperty -LiteralPath $p -Name 'DisplayVersion' -Value $newVer
                Set-ItemProperty -LiteralPath $p -Name 'DisplayName'    -Value "ArtiMax PDF Editor $newVer"
            } catch { }   # HKLM without admin -- cosmetic only, not worth failing the update over
            break
        }
    }

} finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ("Updated {0} -> {1}" -f $curVer, $newVer) -ForegroundColor Green

if (-not $NoLaunch) {
    Write-Host 'Starting PDF Editor...'
    Start-Process -FilePath $destExe
}
