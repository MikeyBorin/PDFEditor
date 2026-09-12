@echo off
REM ======================================================================
REM  ArtiMax PDF Editor -- one-click portable extract, no installer.
REM
REM  For the FULL win-x64 build:
REM    ArtiMaxPDFEditor-<version>-win-x64.zip
REM  from https://github.com/MikeyBorin/PDFEditor/releases/latest
REM
REM  Drop that zip next to this file (normally Downloads) and double-click
REM  this. It will: clear the mark-of-the-web from the zip, extract it,
REM  unblock every file in the extracted PDF Editor folder, put a shortcut
REM  on your Desktop, and start the app.
REM
REM  Nothing is installed -- no Setup, no admin, no Add/Remove Programs
REM  entry. Delete the folder to remove it. Your settings, signatures and
REM  toolbar layout live in %APPDATA%\ArtiMaxPDFEditor\ either way.
REM
REM  By default it extracts into a folder beside this file. To choose a
REM  different home, pass one:
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

$ExeName = 'ArtiMaxPDFEditor.exe'

function Fail($msg) {
    Write-Host ''
    Write-Host $msg -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'ArtiMax PDF Editor -- portable extract' -ForegroundColor Cyan
Write-Host '-------------------------------------'
Write-Host ("Looking in:  {0}" -f $here)

# --- 1. Find the full build zip ---------------------------------------------
# Deliberately *excludes* ArtiMaxPDFEditor-Setup-*.zip -- that one holds an
# installer, not a payload, and belongs to 'Install ArtiMax PDF Editor.bat'.
$zip = Get-ChildItem -LiteralPath $here -Filter 'ArtiMaxPDFEditor-*.zip' -File -ErrorAction SilentlyContinue |
       Where-Object { $_.Name -notlike 'ArtiMaxPDFEditor-Setup-*' } |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $zip) {
    Fail "No ArtiMaxPDFEditor-<version>-win-x64.zip found in:
  $here

Download the full build first:
  https://github.com/MikeyBorin/PDFEditor/releases/latest
and pick the ArtiMaxPDFEditor-<version>-win-x64.zip asset. Save it into this
folder, then double-click this file again.

(ArtiMaxPDFEditor-Setup-<version>.zip is the INSTALLER -- for that one use
'Install ArtiMax PDF Editor.bat'.)"
}

Write-Host ("Zip:         {0}  ({1:N0} MB)" -f $zip.Name, ($zip.Length / 1MB))

# Clear the mark-of-the-web on the ZIP *before* extracting. Windows copies
# that mark onto every file it unpacks, and a marked exe or DLL is what
# triggers SmartScreen -- or fails to load at all, with nothing useful on
# screen to explain why.
Unblock-File -LiteralPath $zip.FullName -ErrorAction SilentlyContinue

# --- 2. Work out where it goes ----------------------------------------------
$dest = $env:ARTIMAX_DEST
if ([string]::IsNullOrWhiteSpace($dest)) {
    $dest = Join-Path $here ([IO.Path]::GetFileNameWithoutExtension($zip.Name))
}
$dest = $dest.TrimEnd('\')

if (Test-Path -LiteralPath $dest) {
    $running = @(Get-Process -Name 'ArtiMaxPDFEditor' -ErrorAction SilentlyContinue |
                 Where-Object { try { $_.Path -like "$dest\*" } catch { $false } })
    if ($running.Count -gt 0) {
        Write-Host ''
        Write-Host 'That copy of PDF Editor is running -- closing it first.' -ForegroundColor Yellow
        foreach ($p in $running) { try { $null = $p.CloseMainWindow(); $null = $p.WaitForExit(5000) } catch { } }
        Get-Process -Name 'ArtiMaxPDFEditor' -ErrorAction SilentlyContinue |
            Where-Object { try { $_.Path -like "$dest\*" } catch { $false } } |
            Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 800
    }
}

Write-Host ("Extracting to: {0}" -f $dest)

# -Force overlays rather than wiping the folder, which deliberately preserves
# extra OCR languages dropped into tessdata\ after a previous extract.
Expand-Archive -LiteralPath $zip.FullName -DestinationPath $dest -Force

# Tolerate a zip with one wrapper folder inside it as well as publish.ps1's
# flat layout, so a hand-made zip still lands somewhere usable.
$appDir = $dest
if (-not (Test-Path -LiteralPath (Join-Path $appDir $ExeName))) {
    $inner = Get-ChildItem -LiteralPath $dest -Directory -ErrorAction SilentlyContinue |
             Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName $ExeName) } |
             Select-Object -First 1
    if ($inner) { $appDir = $inner.FullName }
}

if (-not (Test-Path -LiteralPath (Join-Path $appDir $ExeName))) {
    Fail "That zip doesn't look like an ArtiMax PDF Editor build -- no $ExeName inside:
  $($zip.FullName)

Extracted files were left in $dest for you to look at."
}

# --- 3. Unblock the whole extracted folder ----------------------------------
# The point of the exercise. A blocked DLL in a self-contained .NET app fails
# at load with no visible error, so this sweeps the entire tree, not just the
# exe.
Write-Host 'Unblocking extracted files...'
$files = @(Get-ChildItem -LiteralPath $appDir -Recurse -File -ErrorAction SilentlyContinue)
$files | Unblock-File -ErrorAction SilentlyContinue
Write-Host ("Unblocked:   {0} files" -f $files.Count)

$exe = Join-Path $appDir $ExeName
$ver = ''
try { $ver = ([string](Get-Item -LiteralPath $exe).VersionInfo.FileVersion).Trim() } catch { }
Write-Host ("Version:     {0}" -f $ver)

# --- 4. Desktop shortcut ----------------------------------------------------
# A portable folder with no shortcut means hunting through Downloads every
# time. Overwritten on each extract so it always points at the newest copy.
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
