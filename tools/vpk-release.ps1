<#
  vpk-release.ps1 — build + publish a Velopack release (Setup.exe + delta packages) to GitHub Releases.

  Usage:
    .\tools\vpk-release.ps1 -Version 0.4.0 -Prerelease -Notes "- Fixed X"   # PPE  (channel 'ppe', GitHub pre-release)
    .\tools\vpk-release.ps1 -Version 0.4.0 -Notes "- Fixed X"               # PROD (channel 'win', stable)
    .\tools\vpk-release.ps1 -Version 0.4.0 -PackOnly                        # build + pack locally, NO upload

  PPE builds are only picked up by apps launched with --ppe (UpdateManager ExplicitChannel='ppe').
  PROD uses the default 'win' channel. Promoting a tested PPE build to PROD = run again without -Prerelease
  (same version is fine; it's a separate channel feed) — add -Merge if GitHub complains about the tag.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "",
    [switch]$Prerelease,
    [switch]$PackOnly,
    [switch]$Merge
)
$ErrorActionPreference = 'Stop'
$root    = "C:\Users\jaker\ESOAddons"
$proj    = Join-Path $root "ESOAddons.csproj"
$pub     = Join-Path $root "publish"
$rel     = Join-Path $root "releases"
$icon    = Join-Path $root "appicon.ico"
$repoUrl = "https://github.com/shoyru-ai/eso-addon-manager"
# PPE gets a SEPARATE Velopack identity so its Setup.exe installs side-by-side with PROD instead of
# colliding ("already installed"). PROD's packId is permanent — never change it.
$packId    = if ($Prerelease) { "Shoyru.AddonSuite.PPE" } else { "Shoyru.AddonSuite" }
$packTitle = if ($Prerelease) { "Shoyru's Addon Suite (PPE)" } else { "Shoyru's Addon Suite" }
$mainExe = "Shoyrus Addon Suite.exe"
$channel = if ($Prerelease) { "ppe" } else { "win" }
if (-not $Notes) { $Notes = "Release v$Version" }

# vpk global tool on PATH
$env:Path += ";$env:USERPROFILE\.dotnet\tools"

if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }

Write-Host "Publishing v$Version (self-contained folder) ..." -ForegroundColor Cyan
# Pass the version into the BUILD (not just vpk pack) so the exe's assembly version matches the release —
# otherwise the in-app version badge (UpdateChecker.CurrentVersion reads Assembly.GetName().Version) stays
# frozen at the csproj <Version> regardless of what Velopack ships.
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $pub -p:Version=$Version | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$notesFile = Join-Path $root "release-notes.md"
$Notes | Out-File -Encoding utf8 $notesFile

Write-Host "Packing (channel=$channel) ..." -ForegroundColor Cyan
$packArgs = @("pack", "-u", $packId, "-v", $Version, "-p", $pub, "-e", $mainExe, "-o", $rel, "-c", $channel,
    "--packTitle", $packTitle, "--packAuthors", "Shoyru", "--releaseNotes", $notesFile, "--icon", $icon)

# Code signing via Azure Artifact (Trusted) Signing - only when the metadata file exists (git-ignored).
# Requires az login on this machine with the Certificate Profile Signer role. See azure-signing.example.json.
$signFile = Join-Path $root "tools\azure-signing.json"
if (Test-Path $signFile) {
    Write-Host "Code signing ENABLED (Azure Artifact Signing)" -ForegroundColor Green
    $packArgs += @("--azureTrustedSignFile", $signFile)
} else {
    Write-Host "Code signing DISABLED (no tools/azure-signing.json) - build will be unsigned." -ForegroundColor Yellow
}

vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)" }

if ($PackOnly) {
    Write-Host "PackOnly: built $rel\$packId-$channel-Setup.exe (no upload)." -ForegroundColor Green
    return
}

$token = & "C:\Program Files\GitHub CLI\gh.exe" auth token
$ghArgs = @("upload", "github", "--repoUrl", $repoUrl, "--token", $token, "-o", $rel, "-c", $channel, "--publish", "true")
if ($Prerelease) { $ghArgs += @("--pre", "true") }
if ($Merge)      { $ghArgs += @("--merge", "true") }

Write-Host "Uploading to GitHub (channel=$channel) ..." -ForegroundColor Cyan
vpk @ghArgs
if ($LASTEXITCODE -ne 0) { throw "vpk upload failed ($LASTEXITCODE)" }
Write-Host "Released v$Version  [$channel]." -ForegroundColor Green
