@echo off
REM ======================================================================
REM  Double-click this to update ArtiMax PDF Editor from the newest
REM  ArtiMaxPDFEditor-*.zip sitting in the same folder (normally Downloads).
REM
REM  Keep this file in Downloads next to the zips. It does nothing itself
REM  except call the updater inside the installed app, so it never needs
REM  replacing -- fixes to the update process arrive with the next zip.
REM
REM  It will: unblock the zip, check the version is newer, close the editor,
REM  back up the current install, overlay the new files, and start the app.
REM
REM  Where to get the zip:
REM    https://github.com/MikeyBorin/PDFEditor/releases/latest
REM    -> ArtiMaxPDFEditor-<version>-win-x64.zip
REM ======================================================================

setlocal

set "UPDATER=%LOCALAPPDATA%\Programs\ArtiMax\PDF Editor\scripts\update-from-downloads.ps1"

REM Per-machine installs land under Program Files instead.
if not exist "%UPDATER%" set "UPDATER=%ProgramFiles%\ArtiMax\PDF Editor\scripts\update-from-downloads.ps1"

if not exist "%UPDATER%" (
    echo.
    echo Cannot find the updater script. Looked in:
    echo   %%LOCALAPPDATA%%\Programs\ArtiMax\PDF Editor\scripts\
    echo   %%ProgramFiles%%\ArtiMax\PDF Editor\scripts\
    echo.
    echo If PDF Editor is not installed yet, run
    echo   ArtiMaxPDFEditor-Setup-^<version^>.exe
    echo once -- after that this updater takes over.
    echo.
    echo If it IS installed, this build predates the updater: open the zip,
    echo copy the scripts\ folder into the install folder, then run this again.
    echo.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%UPDATER%" -ZipDir "%~dp0."

echo.
pause
