# MonoTorrent.Client source snapshot

Upstream: https://github.com/alanmcgovern/monotorrent, tag `release-v3.0.2`, commit `e78faebd0aec117146cffccaaea987ab0629eec0`.

Only the client assembly source and its linked helpers are included. The other MonoTorrent assemblies are restored, unmodified, from NuGet 3.0.2. All upstream license headers and the complete MIT license are preserved. This assembly retains the upstream ABI version `2.0.0.0` and identifies the local build as `3.0.2-torrentclassic.1`.

Local changes:

- `MonoTorrent.Client/NetworkIO.cs`: drain each ready queued operation once per pass rather than stopping at the first rate-limited/paused operation. Drop disposed connections with a completion exception. This prevents one paused or disposed torrent from blocking independent transfers in the static I/O queues.
- `Linked/GitInfoHelper.cs`: pin the known upstream version 3.0.2 without the upstream GitInfo build-time generator.
- The project targets .NET 10 and references the existing NuGet component assemblies, Mono.Nat and ReusableTasks. No additional runtime library was introduced by this patch.

Regression coverage is in `tests/TorrentClassic.Tests`: a magnet transfer is paused and its engine disposed, followed by a second two-torrent transfer in the same process with global bandwidth limiting and automatic queue promotion. The upstream FIFO behavior can leave the second transfer blocked behind stale I/O from the first.
