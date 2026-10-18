# Sonar Favorite Picker

A small Windows notification-area app for switching **SteelSeries Sonar → Gaming** to a favorited preset.

- Left-click and right-click the tray icon to open the same menu.
- **About** at the top opens its submenu on click, showing the current app version, **Check for updates**, and **Open on GitHub**. Hovering does not open it; explicit keyboard activation is also supported.
- Lists every Gaming favorite in Sonar's favorite-slot order (including all nine when filled).
- Checkmark identifies the active preset; favorites and selection refresh whenever the menu opens.
- Follows Windows app light/dark mode, with high-contrast support.
- Optional **Start silently with Windows** setting. No window or startup notification.
- Runs as your Windows user, with a single-instance guard and no polling while idle.

## Run

Run `dist\SonarFavoritePicker.exe`. Look for the original pale blue Sonar logo in the notification area; Windows may place it under the hidden-icons arrow. Pin it to the taskbar notification area for quick access. The menu loads before appearing so it opens at its final size without a loading-to-favorites resize.

The lightweight executable uses the shared **.NET 8 Desktop Runtime (x64)**, already installed on this PC. If it is missing, the executable's native Windows launcher shows a download prompt before the app starts. Choosing **Download it now** opens Microsoft's matching Desktop Runtime download page; run the installer and then launch the app again. No custom launcher or bundled runtime is needed. The app has no background polling or startup network requests.

**Check for updates** follows GitHub's public latest-release link only when selected, using a HEAD request without downloading the page or consuming the REST API quota. It compares the latest stable release tag (for example, `v1.1.0`) with the installed version and offers to open a newer release in your browser. Downloads and installation are manual. If no release exists yet, the app says so. **Open on GitHub** opens [the project repository](https://github.com/codeineXO/SonarFavoritePicker).

SteelSeries GG and Sonar must be running. Mark presets as favorites in GG → Sonar → Gaming. The menu reads these directly, so there is no second favorites list to manage. If Sonar starts later or restarts, reopen the menu or select **Refresh favorites** to reconnect.

Enable **Start silently with Windows** from the menu if desired. Keep the executable in a permanent location before enabling it. If you move it, launch the new copy and enable startup again. To uninstall, disable startup, select **Exit**, and delete the app files.

## Build

Requires the .NET 8 SDK on Windows. Publishes a single small x64 executable without bundling the .NET runtime or debugging symbols.

```powershell
.\build.ps1
```

Or pass a specific SDK executable with `-Dotnet C:\path\to\dotnet.exe`.

## Verify

```powershell
Start-Process .\dist\SonarFavoritePicker.exe -ArgumentList '--self-test' -Wait
Get-Content .\dist\self-test-results.txt
.\verify-runtime.ps1
```

The self-test verifies icon transparency and original color, and uses a simulated local API to verify channel filtering, favorite ordering, selection confirmation, rejecting non-Gaming presets, connection discovery and local-address restrictions. It does not change your Sonar presets. The runtime verification starts a child process with an isolated temporary .NET location and checks both missing-runtime cases and the matching Desktop Runtime x64 download URL. Test dialogs are suppressed; installed runtimes and global environment settings are unchanged.

Optional live integration check: run with `--live-test` and read `dist\live-test-results.txt`. This briefly selects another favorite, confirms it through Sonar, and restores the original in a `finally` block. Requires two Gaming favorites and an initially selected favorite. Avoid changing favorites or closing GG during this check.

Manual checks: open the tray menu using both mouse buttons, select a favorite and confirm the checkmark on reopening, add/remove a favorite in GG, change Windows app theme and reopen the menu, enable/disable startup and verify sign-in behavior.

## Integration

Uses GG's local `coreProps.json` → HTTPS `/subApps` discovery, then Sonar's local HTTP `/configs`, `/configs/selected`, and `PUT /configs/{id}/select`. Sonar requests are restricted to loopback; certificate validation is bypassed only for GG's local self-signed certificate. No GG database changes or account access. The optional update check uses normal HTTPS certificate validation and sends no preset information to GitHub. If an automatic check fails, it offers to open the release page in your browser.

This is an independent app using an undocumented API, which SteelSeries updates may change. Endpoint behavior was checked against the installed GG instance. API research reference: [SteelSeries-NET-API](https://github.com/DataNext27/SteelSeries-NET-API). Implementation is original and uses no third-party runtime packages.

The Sonar icon in `assets/` is copied without recoloring from the installed SteelSeries GG `resources/overlays/sonar_logo.png`; the logo belongs to SteelSeries. `generate-icon.ps1` creates PNG-backed icon frames at multiple sizes, preserving alpha transparency and color for both the tray and executable icon. The build script runs this conversion automatically.
