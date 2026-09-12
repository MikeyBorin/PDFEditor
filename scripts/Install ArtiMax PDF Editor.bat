@echo off
REM ======================================================================
REM  ArtiMax PDF Editor -- one-click install from the zipped installer.
REM
REM  For workplaces that block downloading a bare .exe. Grab
REM    ArtiMaxPDFEditor-Setup-<version>.zip
REM  from https://github.com/MikeyBorin/PDFEditor/releases/latest,
REM  drop it next to this file (normally Downloads), and double-click this.
REM
REM  It will: clear the mark-of-the-web from the zip, extract it to a temp
REM  folder, unblock every extracted file, and launch the installer --
REM  so Windows never shows "Windows protected your PC" or a blocked-file
REM  warning along the way.
REM
REM  Keep this file in Downloads. It works for every future release.
REM
REM  Optional: pass  silent  to skip the wizard entirely --
REM    "Install ArtiMax PDF Editor.bat" silent
REM ======================================================================

setlocal
set "ARTIMAX_ZIPDIR=%~dp0"
set "ARTIMAX_MODE=%~1"

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

$silent = ($env:ARTIMAX_MODE -match '^(?i)(silent|/silent|-silent)$')

function Fail($msg) {
    Write-Host ''
    Write-Host $msg -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'ArtiMax PDF Editor -- install' -ForegroundColor Cyan
Write-Host '----------------------------'
Write-Host ("Looking in:  {0}" -f $here)

$stage = $null

# --- 1. Find something to install ------------------------------------------
# A loose Setup.exe beside this script wins: nothing to unzip, just unblock
# it and go. Otherwise fall back to the zipped installer.
$setup = Get-ChildItem -LiteralPath $here -Filter 'ArtiMaxPDFEditor-Setup-*.exe' -File -ErrorAction SilentlyContinue |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $setup) {
    $zip = Get-ChildItem -LiteralPath $here -Filter 'ArtiMaxPDFEditor-Setup-*.zip' -File -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $zip) {
        Fail "No ArtiMaxPDFEditor-Setup-*.zip (or .exe) found in:
  $here

Download the zipped installer first:
  https://github.com/MikeyBorin/PDFEditor/releases/latest
and pick the ArtiMaxPDFEditor-Setup-<version>.zip asset. Save it into this
folder, then double-click this file again.

Note: ArtiMaxPDFEditor-<version>-win-x64.zip is the PORTABLE/update build,
not the installer -- for that one use 'Update ArtiMax PDF Editor.bat'."
    }

    Write-Host ("Zip:         {0}  ({1:N0} MB)" -f $zip.Name, ($zip.Length / 1MB))

    # Clear the mark-of-the-web from the ZIP *before* extracting. Windows
    # stamps that mark onto every file it unpacks, and a marked Setup.exe is
    # exactly what produces the "Windows protected your PC" SmartScreen wall.
    # Unblocking here means the extracted installer is born clean.
    Unblock-File -LiteralPath $zip.FullName -ErrorAction SilentlyContinue

    $stage = Join-Path $env:TEMP ('artimax-install-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Write-Host 'Extracting...'
    Expand-Archive -LiteralPath $zip.FullName -DestinationPath $stage -Force

    # Belt and braces: unblock the whole extracted tree anyway. Some
    # extractors (and some AV shims) re-apply the mark per-file regardless of
    # the zip's own state, and a blocked DLL fails at load time with nothing
    # useful on screen.
    Write-Host 'Unblocking extracted files...'
    Get-ChildItem -LiteralPath $stage -Recurse -File -ErrorAction SilentlyContinue |
        Unblock-File -ErrorAction SilentlyContinue

    $setup = Get-ChildItem -LiteralPath $stage -Recurse -Filter 'ArtiMaxPDFEditor-Setup-*.exe' -File -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $setup) {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
        Fail "That zip holds no ArtiMaxPDFEditor-Setup-*.exe:
  $($zip.FullName)

Nothing was installed."
    }
}

Unblock-File -LiteralPath $setup.FullName -ErrorAction SilentlyContinue

$ver = ''
try { $ver = ([string](Get-Item -LiteralPath $setup.FullName).VersionInfo.FileVersion).Trim() } catch { }
Write-Host ("Installer:   {0}  {1}" -f $setup.Name, $ver)

# --- 2. Run it --------------------------------------------------------------
try {
    $args = @('/NORESTART')
    if ($silent) {
        # /SILENT, not /VERYSILENT: keeps the progress bar so a long unpack
        # doesn't look like nothing happened.
        $args += @('/SILENT', '/SUPPRESSMSGBOXES')
        Write-Host ''
        Write-Host 'Installing (silent)...' -ForegroundColor Green
    } else {
        Write-Host ''
        Write-Host 'Starting the installer...' -ForegroundColor Green
    }

    $proc = Start-Process -FilePath $setup.FullName -ArgumentList $args -PassThru -Wait
    $rc = $proc.ExitCode

    Write-Host ''
    if ($rc -eq 0) {
        Write-Host ("Installed ArtiMax PDF Editor {0}." -f $ver) -ForegroundColor Green
    } elseif ($rc -eq 1602 -or $rc -eq 1223) {
        Write-Host 'Cancelled -- nothing was installed.' -ForegroundColor Yellow
    } else {
        Write-Host ("Setup exited with code {0}." -f $rc) -ForegroundColor Red
    }
    exit $rc
}
finally {
    # The temp copy is dead weight once Setup has finished reading itself.
    if ($stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
