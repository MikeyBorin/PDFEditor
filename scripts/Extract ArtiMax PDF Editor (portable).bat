@echo off
REM ======================================================================
REM  ArtiMax PDF Editor -- one-click portable setup, no installer.
REM
REM  For the FULL build: ArtiMaxPDFEditor-<version>-win-x64.zip
REM  from https://github.com/MikeyBorin/PDFEditor/releases/latest
REM
REM  TWO WAYS TO USE IT, both one click:
REM
REM   1. You just unzipped and this file is sitting in the extracted
REM      folder -- double-click it. It unblocks every file around it,
REM      makes a Desktop shortcut and starts the app.
REM
REM   2. Keep this file in Downloads next to the zip -- double-click it.
REM      It clears the mark-of-the-web from the zip, extracts it, unblocks
REM      the lot, makes the shortcut and starts the app. Works for every
REM      future release, so you never unzip by hand again.
REM
REM  Nothing is installed -- no Setup, no admin, no Add/Remove Programs
REM  entry. Delete the folder to remove it. Settings, signatures and
REM  toolbar layout live in %APPDATA%\ArtiMaxPDFEditor\ either way.
REM
REM  To extract somewhere specific, pass a path:
REM    "Extract ArtiMax PDF Editor (portable).bat" D:\Apps\PDFEditor
REM
REM  NOTE: if PDF Editor is ALREADY installed via Setup, you want
REM  'Update ArtiMax PDF Editor.bat' instead -- that overlays the new build
REM  onto the existing install and keeps your shortcuts.
REM ======================================================================

setlocal
set "ARTIMAX_ZIPDIR=%~dp0"
set "ARTIMAX_DEST=%~1"

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$f=[IO.File]::ReadAllText('%~f0'); iex $f.Substring($f.LastIndexOf('#::PSBEGIN'))"
set "RC=%ERRORLEVEL%"

echo.
pause
exit /b %RC%

#::PSBEGIN
$ErrorActionPreference = 'Stop'

$here = $env:ARTIMAX_ZIPDIR
if ([string]::IsNullOrWhiteSpace($here)) { $here = (Get-Location).Path }
$here = $here.TrimEnd('\')

$ExeName  = 'ArtiMaxPDFEditor.exe'
$ProcName = 'ArtiMaxPDFEditor'

function Fail($msg) {
    Write-Host ''
    Write-Host $msg -ForegroundColor Red
    exit 1
}

function Stop-AppIn($folder) {
    $running = @(Get-Process -Name $ProcName -ErrorAction SilentlyContinue |
                 Where-Object { try { $_.Path -like "$folder\*" } catch { $false } })
    if ($running.Count -eq 0) { return }
    Write-Host 'That copy of PDF Editor is running -- closing it first.' -ForegroundColor Yellow
    foreach ($p in $running) { try { $null = $p.CloseMainWindow(); $null = $p.WaitForExit(5000) } catch { } }
    Get-Process -Name $ProcName -ErrorAction SilentlyContinue |
        Where-Object { try { $_.Path -like "$folder\*" } catch { $false } } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

Write-Host ''
Write-Host 'ArtiMax PDF Editor -- portable' -ForegroundColor Cyan
Write-Host '-----------------------------'

# --- 1. Where is the app? ---------------------------------------------------
# Case A: this .bat ships INSIDE the zip, so on the very first download the
# user unzips by hand in Explorer and runs it from the extracted folder --
# where every file is still carrying the mark-of-the-web. Nothing to extract
# in that case; the unblock sweep below is the whole job.
$appDir = $null

if (Test-Path -LiteralPath (Join-Path $here $ExeName)) {
    $appDir = $here
    Write-Host 'Found the app alongside this file:'
    Write-Host ("  {0}" -f $appDir)
    Stop-AppIn $appDir
}
else {
    # Case B: sitting in Downloads next to the zip. Deliberately EXCLUDES
    # ArtiMaxPDFEditor-Setup-*.zip -- that one holds an installer, not a
    # payload, and belongs to 'Install ArtiMax PDF Editor.bat'.
    Write-Host ("Looking in:  {0}" -f $here)
    $zip = Get-ChildItem -LiteralPath $here -Filter 'ArtiMaxPDFEditor-*.zip' -File -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -notlike 'ArtiMaxPDFEditor-Setup-*' } |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $zip) {
        Fail "No ArtiMaxPDFEditor-<version>-win-x64.zip found in:
  $here

...and no $ExeName beside this file either, so there is nothing to set up.

Download the full build:
  https://github.com/MikeyBorin/PDFEditor/releases/latest
and pick ArtiMaxPDFEditor-<version>-win-x64.zip. Save it into this folder,
then double-click this file again.

(ArtiMaxPDFEditor-Setup-<version>.zip is the INSTALLER -- for that one use
'Install ArtiMax PDF Editor.bat', which is inside it.)"
    }

    Write-Host ("Zip:         {0}  ({1:N0} MB)" -f $zip.Name, ($zip.Length / 1MB))

    # Clear the mark on the ZIP *before* extracting: Windows copies it onto
    # every file it unpacks, and a marked exe trips SmartScreen while a marked
    # DLL in a self-contained .NET app just fails to load, silently.
    Unblock-File -LiteralPath $zip.FullName -ErrorAction SilentlyContinue

    $dest = $env:ARTIMAX_DEST
    if ([string]::IsNullOrWhiteSpace($dest)) {
        $dest = Join-Path $here ([IO.Path]::GetFileNameWithoutExtension($zip.Name))
    }
    $dest = $dest.TrimEnd('\')

    if (Test-Path -LiteralPath $dest) { Stop-AppIn $dest }

    Write-Host ("Extracting to: {0}" -f $dest)
    # -Force overlays rather than wiping, which deliberately preserves extra
    # OCR languages dropped into tessdata\ after an earlier extract.
    Expand-Archive -LiteralPath $zip.FullName -DestinationPath $dest -Force

    # Tolerate a zip with one wrapper folder as well as the flat layout
    # publish.ps1 produces, so a hand-made zip still lands somewhere usable.
    $appDir = $dest
    if (-not (Test-Path -LiteralPath (Join-Path $appDir $ExeName))) {
        $inner = Get-ChildItem -LiteralPath $dest -Directory -ErrorAction SilentlyContinue |
                 Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName $ExeName) } |
                 Select-Object -First 1
        if ($inner) { $appDir = $inner.FullName }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $appDir $ExeName))) {
        Fail "That zip does not look like an ArtiMax PDF Editor build -- no $ExeName inside:
  $($zip.FullName)

Extracted files were left in $dest for you to look at."
    }
}

# --- 2. Unblock the whole folder --------------------------------------------
# The point of the exercise, and why this sweeps the entire tree rather than
# just the exe: one blocked DLL fails at load with nothing on screen.
Write-Host 'Unblocking...'
$files = @(Get-ChildItem -LiteralPath $appDir -Recurse -File -ErrorAction SilentlyContinue)
$files | Unblock-File -ErrorAction SilentlyContinue
Write-Host ("Unblocked:   {0} files" -f $files.Count)

$exe = Join-Path $appDir $ExeName
$ver = ''
try { $ver = ([string](Get-Item -LiteralPath $exe).VersionInfo.FileVersion).Trim() } catch { }
Write-Host ("Version:     {0}" -f $ver)

# --- 3. Desktop shortcut ----------------------------------------------------
# A portable folder with no shortcut means hunting through Downloads every
# time. Named "(portable)" so it can never overwrite the shortcut belonging
# to a copy installed by Setup.
try {
    $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ArtiMax PDF Editor (portable).lnk'
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($lnk)
    $sc.TargetPath       = $exe
    $sc.WorkingDirectory = $appDir
    $sc.Description      = "ArtiMax PDF Editor $ver (portable)"
    $sc.Save()
    Write-Host ("Shortcut:    {0}" -f $lnk)
} catch {
    Write-Host 'Could not create the Desktop shortcut (not fatal).' -ForegroundColor Yellow
}

Write-Host ''
Write-Host ("ArtiMax PDF Editor {0} is ready in:" -f $ver) -ForegroundColor Green
Write-Host ("  {0}" -f $appDir) -ForegroundColor Green
Write-Host ''
Write-Host 'Starting it now...'
Start-Process -FilePath $exe -WorkingDirectory $appDir
exit 0
