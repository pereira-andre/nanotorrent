# NanoTorrent

![NanoTorrent penguin holding a magnet](docs/icon.png)

A **free, portable, open-source BitTorrent client for Windows, macOS and Linux**, inspired by the layout of µTorrent Classic. No ads, subscriptions, accounts, telemetry or embedded browser. The desktop app, remote panel and documentation are in English, with light, dark and system themes.

An independent C# implementation using [MonoTorrent](https://github.com/alanmcgovern/monotorrent), Windows Forms on Windows and [Avalonia](https://github.com/AvaloniaUI/Avalonia) on macOS/Linux. No µTorrent source code, executables, icons or logos are included. This project has no affiliation with µTorrent, BitTorrent or Rainberry.

## Download and run

Download the appropriate archive from [Releases](https://github.com/pereira-andre/nanotorrent/releases/latest) and **extract the complete folder to a writable location**. The .NET runtime is included; no .NET installation or app installer is needed.

| System | Package | Run |
| --- | --- | --- |
| Windows 10/11, x64 | `NanoTorrent-win-x64-portable.zip` | `NanoTorrent.exe` |
| macOS 14+, Apple Silicon | `NanoTorrent-osx-arm64-portable.zip` | `NanoTorrent.app` |
| macOS 14+, Intel | `NanoTorrent-osx-x64-portable.zip` | `NanoTorrent.app` |
| Linux, x64 | `NanoTorrent-linux-x64-portable.tar.gz` | `./NanoTorrent.sh` |
| Linux, ARM64 | `NanoTorrent-linux-arm64-portable.tar.gz` | `./NanoTorrent.sh` |

On macOS, move the extracted **NanoTorrent folder** to a writable location such as your home folder before opening the app. Keep the `.app` inside it: portable data lives beside the bundle. Mac packages have an ad-hoc signature, **not an Apple Developer ID signature or notarization**. macOS may require approval in System Settings → Privacy & Security. Windows builds are unsigned and may also show an operating-system warning.

If macOS launches the app from a temporary read-only location, or the app folder is protected, NanoTorrent now opens a **Choose portable folder** window instead of crashing. Select the extracted NanoTorrent folder (the one containing `Data`, if you already have a session), or another writable folder. Session data stays inside that selected folder. A new session uses its Downloads folder; existing settings and download folders are preserved. The selection is not stored elsewhere; this dialog may reappear while launching from a read-only location. **Preferences → General** shows the active portable data folder. Moving the complete extracted folder to a writable location in Finder keeps the usual portable startup behavior.

Linux packages target glibc systems with an X11 desktop or XWayland, such as Ubuntu 24.04+. Standard system libraries are required: X11, ICE/SM, fontconfig, ICU, OpenSSL and libstdc++. Minimal distributions may need these installed through their package manager. Alpine/musl and headless servers are not targeted. macOS and Linux use a matching classic layout; operating-system controls differ from the Windows Forms version.

The included runtime makes these packages larger than the original µTorrent. Builds are self-contained and untrimmed for compatibility; they are not designed to match its binary size or memory footprint.

NanoTorrent was previously released as Torrent Classic. Existing portable `Data` and `Downloads` folders remain compatible; the repository and application now use the new name.

## Portable data and updates

The first run creates `Data/` (session, settings, RSS, metadata and cache) and `Downloads/` beside the executable on Windows/Linux, or beside the `.app` on macOS. No file associations, startup entries or administrator access are required. Windows may extract native runtime libraries into its normal temporary directory.

**Close the app before moving or updating its folder.** Paths inside the portable folder are stored relative to it and adjusted when the folder moves. External download folders remain absolute. Choose paths appropriate to the destination OS when moving a session across operating systems.

To update, replace the app binaries, docs and licenses with the new package, preserving **`Data` and `Downloads`**. On macOS replace the entire `.app`, preserving the adjacent data folders. The old Windows session and theme settings are migrated automatically. Keep a backup of important data before updating.

## Features

- Real downloading and seeding from `.torrent` files and magnet links, including peer metadata exchange.
- DHT, PEX, local peer discovery, HTTP/HTTPS/UDP trackers and optional UPnP/NAT-PMP through MonoTorrent.
- Start, pause, stop, force re-check and resume after closing; removal keeps downloaded files.
- Search, sortable columns, categories, labels, queue order and single-instance argument forwarding.
- General, Trackers, Peers, Files, Speed and Log tabs with live information.
- File priorities: high, normal, low and do not download.
- Global upload/download limits, listen port and connection limits.
- Create torrents from files or folders with an optional tracker.
- **RSS and Atom:** subscriptions, periodic refresh, ETag/Last-Modified, include/exclude filters, manual or automatic downloads and duplicate history.
- **Weekly scheduler:** 168 hourly slots; normal, limited, seed only or pause; manual pauses are preserved.
- **Automatic queue:** limits for active torrents and downloads, adjustable order, force start and ratio/time seeding goals.
- **Remote control:** responsive browser panel with an access key, torrent uploads, magnets, actions, queue order, file priorities, bandwidth limits and RSS items.
- Persistent byte counters, share ratio and seeding time.

Windows also includes a notification-area icon, close/minimize-to-tray options, drag-and-drop and multiple torrent selection. On macOS/Linux, closing the window saves the session and exits. The app must remain running for automation or remote access.

This implements the main classic workflows with independent code. It does not include µTorrent's cloud Remote service, media player or antivirus.

## RSS

Choose **RSS feeds**, add a subscription, enter its RSS/Atom URL and refresh interval. Select an item and choose **Download selected**. Enable automatic downloads and filters to automate this. Disabled subscriptions stop refreshing automatically.

Filters ignore case: `*` matches any sequence, `?` one character, and `|` separates alternatives. For example, include `Ubuntu*|Debian*` and exclude `*beta*|*arm*`. An empty include filter accepts all titles. Automatic downloads also consider items already present on the first refresh. One failed item does not block the others. Each subscription keeps up to 2000 downloaded IDs and shows up to 500 current items.

## Queue and scheduler

Under **Preferences → Queue**, set the maximum number of active torrents and active downloads. Waiting torrents show **Queued** and start when a slot opens. Use the toolbar arrows to reorder. A seeding goal stops a torrent when it reaches the ratio **or** time limit; 0 disables that goal. Ratio is bytes uploaded divided by the larger of torrent size and bytes received, so existing files can also be seeded toward a useful ratio.

**Force start** bypasses queue limits and seeding goals but respects the scheduler. Forced torrents do not consume normal queue slots. Manually paused or stopped torrents are not restarted by automation.

Under **Preferences → Scheduler**, select a mode and paint the hours with the mouse. The limited mode uses the smaller nonzero limit from global and scheduled limits. Seed only allows complete torrents; re-check existing files first if needed. The grid follows local system time, including daylight-saving changes. It does not start the OS or wake a sleeping computer.

## Remote control

1. Under **Preferences → Remote control**, enable the panel, choose its HTTP port and copy the access key. Save.
2. Use **Options → Open remote control** on this computer.
3. For another device, enable **Allow other devices on the local network / VPN** and open `http://THIS-COMPUTER-IP:PORT/` on that device.
4. Enter the key. It is kept only in that browser session. Generating and saving a new key invalidates the old one.

Remote control is **disabled by default**. Once enabled it accepts only connections from this computer until LAN access is selected. HTTP is intended for a trusted network or VPN; do not expose the port directly to the public Internet. The app does not open the remote port in the router or change firewall rules. Your system firewall may need permission for incoming connections. No cloud account is needed.

The panel accepts magnets, HTTP(S) torrent URLs and `.torrent` uploads; supports start/force/pause/stop/check/remove, queue order, file priorities and bandwidth limits; and can list, refresh and download RSS items. Remote additions use the default download folder. Removing a torrent keeps its files. Desktop preferences remain the place to configure subscriptions, scheduling and remote access.

## Shortcuts

On all desktops: **Ctrl+O** add torrent, **Ctrl+U** add magnet, **Ctrl+P** preferences and **Ctrl+F** search. Use **⌘** instead of Ctrl on macOS. Right-click a torrent for more actions. In Files, select one or more files and right-click to change priorities.

Additional Windows shortcuts: Ctrl+N create torrent, Ctrl+A select all, Delete remove, F5 toggle details, F7 toggle sidebar. Counters are saved when the session changes and on normal exit.

## Build and test

Requires the .NET 10 SDK. The Windows interface runs on Windows; Avalonia provides the macOS/Linux interface.

```sh
dotnet run --project tests/TorrentClassic.Tests -c Release
dotnet build src/TorrentClassic.App -c Release
dotnet build src/TorrentClassic.Desktop -c Release
```

Build the Windows portable package in PowerShell:

```powershell
./scripts/publish.ps1
```

Build macOS packages **on a Mac** (Python 3, Xcode command-line tools for ad-hoc signing):

```sh
python3 scripts/publish-desktop.py osx-arm64
python3 scripts/publish-desktop.py osx-x64
```

Build Linux packages with Python 3 on Linux or macOS:

```sh
python3 scripts/publish-desktop.py linux-x64
python3 scripts/publish-desktop.py linux-arm64
```

Every script includes license texts, bundled third-party notices and SHA256 checksums. CI builds all five packages, verifies native Mac startup/restart and recovery from an actual read-only disk image on both architectures, runs the engine integration suite on Windows/macOS/Linux, renders the real Windows UI, renders the Avalonia UI headlessly on both Mac architectures, and renders it under Xvfb on Linux x64/ARM64. Screenshots cover both themes, RSS, queue, scheduler and remote preferences. Engine tests transfer synthetic data over loopback and check magnet metadata, pause/restart, priorities, SHA256, duplicate detection, removal without deletion, portable relocation, queue promotion after a real limited transfer, schedule policies, seeding goals, RSS/Atom/filter/ETag behavior and the actual authenticated remote HTTP API.

The queue/scheduler tick every 2 seconds. RSS checks due feeds every 30 seconds while respecting each subscription's refresh interval. Remote Kestrel starts only when enabled. The remote panel uses plain HTML/CSS/JavaScript and no external resources. To disable Avalonia's **build-time** statistics when compiling manually, set `AVALONIA_TELEMETRY_OPTOUT=1`; package scripts and CI already do this. Deployed apps do not include build-time collection.

## Licenses and inspiration

App code and project artwork use the [MIT license](LICENSE). Project releases are free. MIT also allows third parties to reuse the code under its terms.

MonoTorrent 3.0.2, Mono.Nat, ReusableTasks and Avalonia retain their original licenses. The MonoTorrent client assembly includes a documented fix for paused/disposed I/O blocking other transfers; see [the source snapshot and local changes](vendor/MonoTorrent.Client/README.md). Other MonoTorrent assemblies are unchanged NuGet binaries. No proprietary client is decompiled.

Packages preserve the dependency copyright notices, the .NET runtime notices, and the Skia/HarfBuzz bundled third-party notices. Inter font files retain the SIL Open Font License. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [licenses/](licenses/).

Visual reference: [µTorrent Classic](https://www.utorrent.com/desktop/), a trademark of its owners. Visual inspiration grants no rights to that brand or its proprietary software. [REA](https://github.com/morluto/rea) was considered as an analysis tool; it is not included or required by this independently implemented app.
