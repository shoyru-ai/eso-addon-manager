<#
  promote-to-prod.ps1 — promote a tested PPE (pre-release) build to PROD.

  Flips the given pre-release to a normal "latest" release. This promotes the EXACT tested
  binary (no rebuild), so what your friends get is byte-for-byte what you validated on PPE.

  Usage:
    .\tools\promote-to-prod.ps1 -Version 0.3.1                 # promote v0.3.1 PPE -> PROD
    .\tools\promote-to-prod.ps1                                # promote the newest pre-release

  After this, the public /releases/latest (and the 'latest/download' link) serve this build,
  and every PROD app updates to it.
#>
param(
    [string]$Version = ""
)
$ErrorActionPreference = 'Stop'
$gh   = "C:\Program Files\GitHub CLI\gh.exe"
$repo = "shoyru-ai/eso-addon-manager"

if (-not $Version) {
    # newest pre-release by published date
    $json = & $gh release list --repo $repo --json tagName,isPrerelease,createdAt -L 30 | ConvertFrom-Json
    $pre  = $json | Where-Object { $_.isPrerelease } | Sort-Object createdAt -Descending | Select-Object -First 1
    if (-not $pre) { throw "No pre-releases found to promote." }
    $tag = $pre.tagName
} else {
    $tag = if ($Version -like "v*") { $Version } else { "v$Version" }
}

Write-Host "Promoting $tag : pre-release -> PROD (latest) ..." -ForegroundColor Cyan
& $gh release edit $tag --repo $repo --prerelease=false --latest
if ($LASTEXITCODE -ne 0) { throw "gh release edit failed (exit $LASTEXITCODE)" }
Write-Host "$tag is now PROD (latest). PROD apps will update to it." -ForegroundColor Green
& $gh release list --repo $repo -L 5
