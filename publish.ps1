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

$Stem    = "PDFEditor-$Version-$Runtime"
$Staging = Join-Path $DistDir $Stem

Write-Host "Building $Stem..." -ForegroundColor Cyan

# --- Clean staging (keep any older ZIPs) ------------------------------------
if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item -ItemType Directory -Path $Staging | Out-Null

# --- Publish -----------------------------------------------------------------
& dotnet publish $Project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $Staging `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# --- Bundle user-facing files at the top level ------------------------------
Copy-Item -Path (Join-Path $RepoRoot "LICENSE")   -Destination $Staging -Force
Copy-Item -Path (Join-Path $RepoRoot "README.md") -Destination $Staging -Force

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
    Write-Host "Attach it to a GitHub Release, or share the file directly." -ForegroundColor DarkGray
} else {
    Write-Host ""
    Write-Host "Staging folder ready: $Staging" -ForegroundColor Green
}
