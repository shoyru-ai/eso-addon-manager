<#
  cut-release.ps1 — build a self-contained single-file exe for the given version
  and publish it as a GitHub Release (with the exe + a zip as assets).

  Usage:
    .\tools\cut-release.ps1 -Version 0.2.0 -Notes "- Fixed X`n- Added Y"       # PROD (stable, becomes 'latest')
    .\tools\cut-release.ps1 -Version 0.3.1 -Prerelease -Notes "..."             # PPE (staging, NOT 'latest')

  PROD releases are what the public app updates to (via /releases/latest).
  -Prerelease publishes a STAGING build that only PPE-channel apps (--ppe) pick up; the public
  'latest' link ignores it. Promote a tested PPE build to PROD with tools/promote-to-prod.ps1.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "",
    [switch]$Prerelease
)
$ErrorActionPreference = 'Stop'
$gh   = "C:\Program Files\GitHub CLI\gh.exe"
$proj = "C:\Users\jaker\ESOAddons\ESOAddons.csproj"
$dist = "C:\Users\jaker\ESOAddons\dist"
$app  = Join-Path $dist "app"
$repo = "shoyru-ai/eso-addon-manager"
if (-not $Notes) { $Notes = "Release v$Version" }

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $app | Out-Null

Write-Host "Building self-contained single-file v$Version ..." -ForegroundColor Cyan
# NOTE: EnableCompressionInSingleFile is intentionally OFF. The compressed/self-extracting
# bundle trips Windows Defender's ML heuristic (Trojan:Win32/Ulthar.A!ml false positive).
# Uncompressed single-file scans clean (verified 2026-06-26). Bigger exe (~154 MB) but downloadable.
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false -p:Version=$Version -o $app | Out-Null
Remove-Item (Join-Path $app "*.pdb") -Force -ErrorAction SilentlyContinue

@"
Shoyru Addon Suite - the complete addon manager for The Elder Scrolls Online
============================================================================

HOW TO RUN
  Double-click "Shoyru Addon Suite.exe". Nothing to install (.NET is bundled).
  First launch: Windows SmartScreen may warn (unsigned) -> More info -> Run anyway.

REQUIREMENTS
  Windows 10/11 64-bit, and ESO installed + launched once.

ADDON FOLDER
  Auto-detected at Documents\Elder Scrolls Online\live\AddOns (incl. OneDrive).
  If not found, click "Change folder..." (top-right) and pick it.

UPDATES
  On launch it checks GitHub for a newer version and offers a one-click update.

Data comes from the public ESOUI (mmoui) API. No account needed.
"@ | Out-File -Encoding utf8 (Join-Path $app "README.txt")

# Clean-named assets (no apostrophe/space) for the release
$assetExe = Join-Path $dist "Shoyru-Addon-Suite.exe"
Copy-Item (Join-Path $app "Shoyru Addon Suite.exe") $assetExe -Force
$zip = Join-Path $dist "Shoyru-Addon-Suite-v$Version.zip"
Compress-Archive -Path (Join-Path $app '*') -DestinationPath $zip -Force

$channel = if ($Prerelease) { "PPE (pre-release)" } else { "PROD (latest)" }
Write-Host "Creating GitHub release v$Version  [$channel] ..." -ForegroundColor Cyan
$notesFile = Join-Path $dist "release-notes.txt"
$Notes | Out-File -Encoding utf8 $notesFile
$ghArgs = @("release","create","v$Version",$assetExe,$zip,"--repo",$repo,"--title","v$Version","--notes-file",$notesFile)
if ($Prerelease) { $ghArgs += "--prerelease" } else { $ghArgs += "--latest" }
& $gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create failed (exit $LASTEXITCODE)" }
Write-Host "Released v$Version  [$channel]." -ForegroundColor Green
