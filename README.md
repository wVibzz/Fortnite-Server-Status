# Fortnite Status

A small widget that shows if Fortnite is launchable from the Epic Games Launcher, plus the live health of every Fortnite backend service.

## Building from source

Double-click **`build.bat`**, which compiles `FortniteStatus.cs` into `FortniteStatus.exe`.
It runs on the .NET Framework 4.8 that is built into Windows 10 (1903+) and Windows 11.

### Requirements

- Windows 10 (version 1903 or newer) or Windows 11.
- The .NET Framework 4.8 it needs should already be installed on Windows.

## Running it

Run **`FortniteStatus.exe`**.
The first time you run the built `.exe`, Windows may show "Windows protected your PC." Click **More info**, then **Run anyway**.

Click the gear icon (top right) to open settings:
Your settings are saved to `%APPDATA%\FortniteStatus\config.json`.

<img width="352" height="270" alt="image" src="https://github.com/user-attachments/assets/cbe65e64-502c-468e-bb32-e12676930b62" />


## How it works

The app polls two Epic endpoints every second:

- **Lightswitch** (`lightswitch-public-service-prod.ol.epicgames.com`) for the authoritative up/down launch gate. This requires an app-level OAuth token obtained with Epic's public Fortnite game-client credentials.
- **Status page** (`status.epicgames.com/api/v2/components.json`) for per-service health.

## Notes

- Windows only.
- If a check fails (no internet, or an Epic endpoint is down), the title bar shows "ping failed" and the launch gate reads "Unknown".

## Also useful

- `status.epicgames.com`
