<#
.SYNOPSIS
    Build a portable release ZIP of PDF Editor for Windows.

.DESCRIPTION
    Publishes a self-contained single-file exe (no .NET install needed on the
    target machine), copies LICENSE + README + optional tessdata alongside it,
    and produces a ZIP under .\dist\ ready to attach to a GitHub Release.

.PARAMETER Version
    Version stamp used in the output filename. Defaults to `git describe`
    (with fallback to "dev-<yyyyMMdd>") so untagged builds still work.

.PARAMETER Runtime
    RID for dotnet publish. Defaults to win-x64.

.PARAMETER TessdataPath
    Folder containing tessdata files (e.g. eng.traineddata) to bundle for OCR.
    Optional — the script also probes a few common locations and warns if
    nothing is found.

.PARAMETER SkipZip
    Build the staging folder but skip the ZIP step. Useful for smoke-testing
    the publish before committing to a release artifact.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Version 1.0.0
    .\publish.ps1 -Version 1.0.0 -TessdataPath C:\ocr\tessdata
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Runtime = "win-x64",
    [string]$TessdataPath,
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$Project  = Join-Path $RepoRoot "PDFEditor\PDFEditor.csproj"
$DistDir  = Join-Path $RepoRoot "dist"

# --- Version -----------------------------------------------------------------
if (-not $Version) {
    $desc = $null
    try { $desc = (& git -C $RepoRoot describe --tags --always --dirty 2>$null) } catch { }
    if ($desc) { $Version = $desc.Trim() } else { $Version = "dev-" + (Get-Date -Format "yyyyMMdd") }
}

$Stem    = "ArtiMaxPDFEditor-$Version-$Runtime"
$Staging = Join-Path $DistDir $Stem

Write-Host "Building $Stem..." -ForegroundColor Cyan

# --- Clean staging (keep any older ZIPs) ------------------------------------
if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item -ItemType Directory -Path $Staging | Out-Null

# --- Publish -----------------------------------------------------------------
# NOTE: deliberately NOT using -p:PublishSingleFile=true. Single-file worked
# fine at home but broke at least one office PC where AppLocker / SmartScreen
# / corporate antivirus blocked the runtime extraction of the Tesseract native
# DLLs (tesseract50.dll, leptonica-*.dll) to %TEMP%\.net\ArtiMaxPDFEditor\.
# Loose-file publish puts every DLL beside the exe, installs into Program
# Files via Setup.exe, and side-steps the whole extract-and-load-from-Temp
# chain that made OCR fail with a bare "TargetInvocationException" on
# locked-down machines.
& dotnet publish $Project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $Staging `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# --- Bundle user-facing files at the top level ------------------------------
Copy-Item -Path (Join-Path $RepoRoot "LICENSE")   -Destination $Staging -Force
Copy-Item -Path (Join-Path $RepoRoot "README.md") -Destination $Staging -Force

# --- Update scripts ---------------------------------------------------------
# Shipped INSIDE the payload so the updater travels with the app: the .bat the
# user keeps in Downloads only calls the copy under the install folder, which
# means a fix to the update process arrives with the next release rather than
# needing a separate hand-off.
$scriptsSrc = Join-Path $RepoRoot "scripts"
if (Test-Path $scriptsSrc) {
    $scriptsDest = Join-Path $Staging "scripts"
    New-Item -ItemType Directory -Path $scriptsDest -Force | Out-Null
    Copy-Item -Path (Join-Path $scriptsSrc "*") -Destination $scriptsDest -Recurse -Force
    Write-Host "  bundled update scripts" -ForegroundColor Green

    # The launchers also go at the ZIP's TOP LEVEL, not just in scripts\.
    # Only zips ship, so the first unpack has to be done by hand in Explorer
    # -- and what the user needs to find at the top of that extraction is the
    # .bat. From then on they keep it in Downloads and never unzip manually
    # again. Portable first, since that's the zip this one belongs to.
    foreach ($launcher in @('Extract ArtiMax PDF Editor (portable).bat', 'Install ArtiMax PDF Editor.bat')) {
        $lp = Join-Path $scriptsSrc $launcher
        if (Test-Path $lp) { Copy-Item $lp -Destination $Staging -Force }
        else { Write-Warning "scripts\$launcher is missing -- it won't be at the top of the ZIP." }
    }
} else {
    Write-Warning "No scripts folder found -- the release will have no updater in it."
}

# --- tessdata (OCR training data) -- optional -------------------------------
$search = @()
if ($TessdataPath) { $search += $TessdataPath }
$search += Join-Path $RepoRoot "PDFEditor\tessdata"
$search += Join-Path $RepoRoot "tessdata"
$search += Join-Path $RepoRoot "PDFEditor\bin\Debug\net9.0-windows\tessdata"

$tessFound = $search | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($tessFound) {
    $tessDest = Join-Path $Staging "tessdata"
    New-Item -ItemType Directory -Path $tessDest -Force | Out-Null
    Copy-Item -Path (Join-Path $tessFound "*") -Destination $tessDest -Recurse -Force
    Write-Host "  bundled tessdata from $tessFound" -ForegroundColor Green
} else {
    Write-Warning "No tessdata folder found. OCR will not work in the shipped build until the user"
    Write-Warning "drops eng.traineddata into a 'tessdata' folder next to PDFEditor.exe."
    Write-Warning "Source: https://github.com/tesseract-ocr/tessdata"
}

# --- ZIP ---------------------------------------------------------------------
if (-not $SkipZip) {
    $zip = Join-Path $DistDir "$Stem.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $Staging "*") -DestinationPath $zip -CompressionLevel Optimal
    $sizeMB = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Wrote $zip  ($sizeMB MB)" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Staging folder ready: $Staging" -ForegroundColor Green
}

# --- Installer (Inno Setup) -- best effort ----------------------------------
# Wraps the same staging folder into a familiar Windows Setup.exe (Start Menu
# shortcut, Add/Remove Programs entry, optional file association). If Inno
# Setup isn't installed we skip and just note it -- the ZIP is still enough
# to publish.
$issScript = Join-Path $RepoRoot "installer\ArtiMaxPDFEditor.iss"
if (Test-Path $issScript) {
    $iscc = $null
    foreach ($p in @(
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    )) { if ($p -and (Test-Path $p)) { $iscc = $p; break } }
    if (-not $iscc) {
        $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }

    if ($iscc) {
        Write-Host ""
        Write-Host "Building Setup.exe via Inno Setup..." -ForegroundColor Cyan
        & $iscc "/Qp" "/DAppVersion=$Version" $issScript
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Inno Setup exited with code $LASTEXITCODE. Setup.exe was not produced."
        } else {
            $setupExe = Join-Path $DistDir "ArtiMaxPDFEditor-Setup-$Version.exe"
            if (Test-Path $setupExe) {
                $setupMB = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
                Write-Host "Wrote $setupExe  ($setupMB MB)" -ForegroundColor Green

                # --- Zipped installer ------------------------------------
                # Plenty of corporate gateways refuse a bare .exe download
                # outright. This is the same Setup.exe wrapped in a zip, with
                # the one-click extract-unblock-and-run .bat beside it, so the
                # user never has to clear mark-of-the-web by hand.
                $setupZip = Join-Path $DistDir "ArtiMaxPDFEditor-Setup-$Version.zip"
                if (Test-Path $setupZip) { Remove-Item $setupZip -Force }

                $zipItems   = @($setupExe)
                $installBat = Join-Path $scriptsSrc "Install ArtiMax PDF Editor.bat"
                if (Test-Path $installBat) {
                    $zipItems += $installBat
                    # Also drop it loose in dist. It has to be downloadable on
                    # its own, because it is the thing that opens the zip --
                    # shipping it only inside the zip would be circular.
                    Copy-Item $installBat -Destination $DistDir -Force
                } else {
                    Write-Warning "scripts\Install ArtiMax PDF Editor.bat is missing -- the zipped installer will have no one-click launcher."
                }

                Compress-Archive -Path $zipItems -DestinationPath $setupZip -CompressionLevel Optimal
                $setupZipMB = [math]::Round((Get-Item $setupZip).Length / 1MB, 1)
                Write-Host "Wrote $setupZip  ($setupZipMB MB)" -ForegroundColor Green
            }
        }
    } else {
        Write-Host ""
        Write-Warning "Inno Setup not found -- skipping Setup.exe build. Install from https://jrsoftware.org/isdl.php and re-run publish.ps1 to include it in the next build."
    }
}

Write-Host ""
Write-Host "Ship ZIPS ONLY:" -ForegroundColor DarkGray
Write-Host "  dist\$Stem.zip                          (portable / update)" -ForegroundColor DarkGray
Write-Host "  dist\ArtiMaxPDFEditor-Setup-$Version.zip  (installer)" -ForegroundColor DarkGray
Write-Host "Each carries its own launcher .bat at the top level. The bare" -ForegroundColor DarkGray
Write-Host "Setup .exe stays out of the release -- gateways block it." -ForegroundColor DarkGray

# --- Clean up the staging folder -------------------------------------------
# Staging is a build intermediate: the ZIP and Setup.exe both contain the
# same files. Keeping it around after a successful build just wastes ~90 MB
# per release. If -SkipZip was passed we KEEP staging because in that mode
# it *is* the deliverable.
if (-not $SkipZip -and (Test-Path $Staging)) {
    Remove-Item $Staging -Recurse -Force
    Write-Host "Cleaned up staging folder ($Stem)." -ForegroundColor DarkGray
}
