# Shoyru's ESO Addons

A fast, lightweight **addon manager for The Elder Scrolls Online** — browse, install, update, and remove addons from [ESOUI](https://www.esoui.com), with none of the bloat. A lean alternative to Minion (ESO-only, no ads, no extra games).

> Built with WPF on .NET 8.

---

## Features

- **Installed tab** — see every addon in your `live\AddOns` folder with one-click **Update**, **Update All**, and **Remove**.
- **Browse ESOUI** — search the full catalog, filter by **category**, and sort by **downloads / recently updated / name** (clickable column headers with ▲▼ too).
- **Detail pane** — addon image, full description, **changelog ("What's New")**, and **dependencies** shown with ✓ installed / ✗ missing (plus a **Get** button to grab a missing one).
- **Automatic dependency install** — installing an addon pulls the libraries it needs.
- **Reliable update detection** — records the ESOUI version it installs and compares like-for-like, so update buttons clear correctly after updating.
- **Smart folder detection** — finds your AddOns folder automatically (including OneDrive-redirected Documents); a **Change folder…** button if you keep it elsewhere.
- **Self-updating** — checks GitHub on launch and offers a one-click update to the latest release.

## Install

1. Download the latest **`Shoyrus-ESO-Addons.exe`** (or the zip) from the [**Releases**](../../releases) page.
2. Double-click it — **nothing else to install** (.NET is bundled in the exe).
3. First launch: if Windows SmartScreen warns (the app isn't code-signed), click **More info → Run anyway**.

**Requirements:** Windows 10/11 64-bit, and ESO installed + launched at least once (so the AddOns folder exists).

## Updating

On launch the app checks the GitHub Releases here for a newer version. If one exists, a banner appears — click **Update now** and it downloads, swaps itself, and relaunches on the new version.

## Building from source

```powershell
git clone https://github.com/shoyru-ai/eso-addon-manager.git
cd eso-addon-manager
dotnet build -c Release          # build
dotnet test ..\ESOAddonsTests    # run the test suite
```

Cut a release (self-contained single-file exe + zip, published to GitHub Releases):

```powershell
.\tools\cut-release.ps1 -Version 0.2.0 -Notes "What changed"
```

## How it works

Addon data comes from the public **ESOUI / mmoui v3 API** (`api.mmoui.com/v3/game/ESO/…`) — no account or key needed. Your own custom addons that aren't on ESOUI still appear in the Installed list; junctioned/linked addons are protected from accidental removal.
