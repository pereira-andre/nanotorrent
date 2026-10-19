# Third-party notices

Torrent Classic application source: MIT, copyright © 2026 André Pereira and contributors. Original application icon and toolbar glyphs were drawn for this project.

| Distributed dependency | Version | License text |
| --- | --- | --- |
| [MonoTorrent](https://github.com/alanmcgovern/monotorrent) | 3.0.2 | [MIT](licenses/MonoTorrent-LICENSE.txt) |
| [Mono.Nat](https://github.com/mono/Mono.Nat) | 3.0.0 | [MIT](licenses/Mono.Nat-LICENSE.txt) |
| [ReusableTasks](https://github.com/alanmcgovern/ReusableTasks) | 4.0.0 | [MIT](licenses/ReusableTasks-LICENSE.txt) |
| [.NET runtime](https://github.com/dotnet/runtime) | recorded in the release build | MIT and bundled third-party notices; copied into the portable package by `scripts/publish.ps1` |
| [Windows Desktop runtime](https://github.com/dotnet/winforms) | recorded in the release build | Microsoft license text copied into the portable package by `scripts/publish.ps1` |
| [ASP.NET Core / Kestrel](https://github.com/dotnet/aspnetcore) | recorded in the release build | Microsoft license and bundled notices copied from the runtime package by `scripts/publish.ps1` |

The dependency license files are copied verbatim from the NuGet packages; retain them when redistributing source or binary packages. The package script also retains the .NET runtime third-party notices. Original third-party copyright notices remain authoritative.

No µTorrent/Rainberry source code, binaries, images, icons or logos are distributed. µTorrent is mentioned solely to identify the interface inspiration. There is no affiliation or endorsement. REA is not a runtime or build dependency.

The MonoTorrent.Client assembly is built from the MIT source snapshot at upstream commit `e78faebd0aec117146cffccaaea987ab0629eec0`, with a local I/O queue fix and .NET 10 build adaptation. Its informational version is `3.0.2-torrentclassic.1`. Source and exact changes are in `vendor/MonoTorrent.Client`; binary packages include `licenses/MonoTorrent-LOCAL-CHANGES.md`.

macOS and Linux distributions additionally include:

| Dependency | Version | License / retained notice |
| --- | --- | --- |
| Avalonia (desktop, platform backends, Simple theme, headless rendering) | 12.1.4 | MIT, `licenses/Avalonia-LICENSE.txt` |
| Avalonia DataGrid | 12.1.2 | MIT, same Avalonia notice |
| Avalonia.BuildServices (build only) | 11.3.2 | MIT, `licenses/Avalonia.BuildServices-LICENSE.txt` |
| MicroCom.Runtime | 0.11.6 | MIT, `licenses/MicroCom-LICENSE.txt` |
| Tmds.DBus.Protocol | 0.94.1 | MIT, `licenses/Tmds.DBus-LICENSE.txt` |
| Inter font | bundled by Avalonia.Fonts.Inter | SIL Open Font License 1.1, `licenses/Inter-OFL.txt` |
| SkiaSharp | 3.119.4 | MIT wrapper and bundled third-party notices copied verbatim from NuGet |
| HarfBuzzSharp | 8.3.1.3 | MIT wrapper and bundled third-party notices copied verbatim from NuGet |

`scripts/publish-desktop.py` retains every restored package's supplied license/notice files (including notices for native Skia and HarfBuzz components), records package versions and license declarations in `licenses/PACKAGE-MANIFEST.md`, and copies Microsoft runtime licenses/notices. The manifest also lists build-only and other-platform dependencies for transparency. The original third-party texts, including BSD, MIT, Apache and font notices carried by those packages, continue to apply to their respective components.
