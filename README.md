# WindowBouncer

Close all open windows from the system tray or a global hotkey.

WindowBouncer sits in your system tray and gives you a single click (or keypress) to dismiss every open window on the desktop — useful for clearing your screen before a presentation, screenshare, or just a clean break.

## Features

- **Close All** — send WM_CLOSE to every visible window at once, with automatic dismissal of blocking confirmation dialogs
- **Selective close** — check individual rows and close only the ones you want
- **Global hotkey** — trigger Close All from any app without switching focus
- **System tray** — runs quietly in the background; double-click to show, right-click for the context menu
- **Minimize to tray** — closing the main window hides it rather than exiting (configurable)
- **Window filter** — type to narrow the list by title, process name, or PID
- **Exclusions** — permanently exclude processes or title patterns from appearing in the list
- **Light / Dark / System theme**
- **Start with Windows** — optional autostart via registry or, when elevated, a scheduled task
- **Run as admin** — elevates via a pre-registered scheduled task so UAC prompts are avoided on launch

## Requirements

- Windows 10 (1809) or later
- x64

## Installation

Download `WindowBouncer-<version>-setup.exe` from [Releases](https://github.com/redeuxx/WindowBouncer/releases) and run it. No administrator rights required. The installer offers optional desktop shortcut and start-with-Windows entries.

Settings are stored in `%APPDATA%\WindowBouncer\settings.json` and are preserved across upgrades and reinstalls.

## Usage

| Action | How |
|--------|-----|
| Show / hide main window | Double-click tray icon |
| Close all windows | Tray menu → **Close All Windows**, or configured hotkey |
| Close selected windows | Check rows in the list → **Close Selected** |
| Close one window | Click the **×** button on any row |
| Configure settings | Tray menu → **Settings**, or main window toolbar → **Settings** |
| Refresh the window list | Toolbar → **Refresh**, or reopen the main window |

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Theme | System | Light, Dark, or follow the system setting |
| Close to tray | On | Closing the main window hides it; use tray → Exit to quit |
| Start minimized | Off | Launch directly to tray without showing the main window |
| Start with Windows | Off | Register autostart at login |
| Run as admin | Off | Launch elevated via a scheduled task (avoids per-launch UAC) |
| Close All hotkey | (none) | Global hotkey to trigger Close All from any app |
| Excluded processes | `windowbouncer` | Process names never shown in the list |
| Excluded title patterns | (none) | Substring or glob patterns matched against window titles |

## CLI Flags

```
WindowBouncer.exe [/NOUI] [/CLOSE] [/REGISTERSTARTUP]
```

| Flag | Effect |
|------|--------|
| `/NOUI` | Start without showing the main window (tray only) |
| `/CLOSE` | Close all open windows silently and exit — no UI shown |
| `/REGISTERSTARTUP` | Silently register the application to start with Windows at login |

## Building

Prerequisites: .NET 10 SDK, [Inno Setup 6](https://jrsoftware.org/isinfo.php) (for the EXE installer), [Windows SDK](https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/) (for the MSIX — provides `makeappx.exe`).

WindowBouncer is a WinUI 3 / Windows App SDK app. Both the EXE installer and the MSIX bundle the WindowsAppSDK runtime, so end users don't have to install it separately.

```powershell
# Both EXE installer and MSIX
./build-installer.ps1

# EXE installer only
./build-installer.ps1 -SkipMsix

# MSIX only
./build-installer.ps1 -SkipExe

# Fast debug build (skips packaging entirely)
./build-installer.ps1 -DebugOnly

# Override tool paths (if not installed to default locations)
./build-installer.ps1 -InnoSetupCompiler "D:\Tools\ISCC.exe" -MakeAppx "D:\SDK\makeappx.exe"
```

Output lands in `publish/`.

## Data & Privacy

WindowBouncer does not collect or transmit any data. The only files it writes are:

- `%APPDATA%\WindowBouncer\settings.json` — user preferences
- `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` — autostart entry (optional)
- A Windows Scheduled Task named `WindowBouncer` (optional, admin mode only)
